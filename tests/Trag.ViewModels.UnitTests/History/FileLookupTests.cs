using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.History;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.History;

// What a file's menu looks up: its history in the History section, and the file blamed, in the working tree from the
// file status or at the commit from that commit's files. A file Git does not track has no history, a file that is not
// there has no blame, and a name that is not UTF-8 reaches Git as neither.
public sealed class FileLookupTests
{
    private static FileDiffMetadata Changed(string path, string status = "M") => new(Snapshots.Path(path), status, "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    private static async Task<ShellViewModel> OpenedAsync(ShellHarness h)
    {
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        return model;
    }

    [Fact]
    public async Task WhenAStatusFileIsLookedUp_ShouldShowItsHistoryOrBlameItInTheWorkingTree()
    {
        var h = new ShellHarness();
        await using var model = await OpenedAsync(h);
        var row = new StatusRow(Snapshots.Modified("src/a.txt"), false);

        model.History.ShowStatusHistoryCommand.Execute(row);
        await model.History.HistoryCompletion;
        Assert.Equal((WorkspaceSection.History, "src/a.txt"), (model.Navigator.Section, h.History.LastPath));

        await model.Blame.BlameStatusCommand.ExecuteAsync(row);
        Assert.Equal((true, "src/a.txt", BlameText.Version(null)), (model.Blame.IsOpen, model.Blame.Path, model.Blame.Version));
    }

    [Fact]
    public async Task WhenAStatusFileIsUntrackedDeletedOrNotUtf8_ShouldOfferOnlyWhatGitCanAnswer()
    {
        await using var model = await OpenedAsync(new ShellHarness());
        var (untracked, deleted, staged) = (new StatusRow(new FileStatus(Snapshots.Path("new.txt"), '?', '?'), false),
            new StatusRow(new FileStatus(Snapshots.Path("gone.txt"), '.', 'D'), false), new StatusRow(new FileStatus(Snapshots.Path("removed.txt"), 'D', '.'), true));
        var latin = new StatusRow(new FileStatus(new GitPath([0x63, 0xE8, 0x2E, 0x74]), '.', 'M'), false);

        Assert.Equal([false, true, true, false], new[] { untracked, deleted, staged, latin }.Select(row => model.History.ShowStatusHistoryCommand.CanExecute(row)));
        Assert.Equal([false, false, false, false], new[] { untracked, deleted, staged, latin }.Select(row => model.Blame.BlameStatusCommand.CanExecute(row)));
    }

    // Git blames the working tree from HEAD, so before the first commit a staged file has nothing to be blamed from; once
    // the first commit is there, Blame is offered again.
    [Fact]
    public async Task WhenTheRepositoryHasNoCommitYet_ShouldNotOfferToBlameAStagedFile()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(unborn: true, files: [Snapshots.Staged("a.txt", 'A')]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var row = (StatusRow)model.FileStatus.StagedFiles[0];
        var asked = 0;
        model.Blame.BlameStatusCommand.CanExecuteChanged += (_, _) => asked++;

        Assert.False(model.Blame.BlameStatusCommand.CanExecute(row));
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.True(model.Blame.BlameStatusCommand.CanExecute(row));
        Assert.NotEqual(0, asked);
    }

    // A commit's file is looked up by the name it has in that commit; a file the commit deleted is not there to blame.
    [Fact]
    public async Task WhenACommitsFileIsLookedUp_ShouldShowItsHistoryOrBlameItAtThatCommit()
    {
        var h = new ShellHarness();
        h.History.Details = id => new(Snapshots.Commit(id, "Change"), "Body", [Changed("a.txt"), Changed("gone.txt", "D")]);
        await using var model = await OpenedAsync(h);
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;
        var (kept, gone) = (model.History.Details.Panel.Files[0], model.History.Details.Panel.Files[1]);

        var latin = new CommitFileRow(new(new GitPath([0x63, 0xE8, 0x2E, 0x74]), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2')), Snapshots.Id('c'));
        Assert.Equal((false, false), (model.History.ShowCommitFileHistoryCommand.CanExecute(latin), model.Blame.BlameCommitFileCommand.CanExecute(latin)));
        Assert.False(model.Blame.BlameCommitFileCommand.CanExecute(gone));
        await model.Blame.BlameCommitFileCommand.ExecuteAsync(kept);
        Assert.Equal(("a.txt", BlameText.Version(model.History.Page.Commits[0].Id)), (model.Blame.Path, model.Blame.Version));

        model.History.ShowCommitFileHistoryCommand.Execute(gone);
        await model.History.HistoryCompletion;
        Assert.Equal("gone.txt", h.History.LastPath);
    }
}
