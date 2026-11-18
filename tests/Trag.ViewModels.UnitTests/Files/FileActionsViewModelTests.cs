using Trag.Domain;
using Trag.Domain.Diff;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.History;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Files;

// A file's own menu: opened in its program, shown in the file manager, its path copied, an untracked one ignored, or a
// commit's version saved or restored, or what a commit did to it undone. A file that is not in the working tree says so, and one that would run is not opened.
public sealed class FileActionsViewModelTests
{
    private static CommitFileRow CommitFile(string path, string status = "M") => new(status == "D"
        ? new FileDiffMetadata(Snapshots.Path(path), "D", "100644", "000000", Snapshots.Id('1'), new string('0', 40))
        : new FileDiffMetadata(Snapshots.Path(path), status, "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2')), Snapshots.Id('c'));

    private static async Task<(ShellViewModel Model, RecordingHost Host)> OpenedAsync()
    {
        var model = new ShellHarness().Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var host = new RecordingHost();
        model.Host = host;
        return (model, host);
    }

    [Fact]
    public async Task WhenAFileIsOpenedRevealedOrCopied_ShouldHandTheWorkingTreesFileToTheShell()
    {
        var (model, host) = await OpenedAsync();
        await using var _ = model;
        var path = Snapshots.Path("src/a b.txt");

        model.Files.OpenCommand.Execute(path);
        model.Files.RevealCommand.Execute(path);
        model.Files.CopyPathCommand.Execute(path);

        var full = WorkingFile.PathIn(model.Repository.Snapshot!.Root, path);
        Assert.Equal([("open", full), ("reveal", full), ("copy", full)], host.Calls);
        Assert.Equal("", model.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenTheFileIsNotInTheWorkingTree_ShouldSaySo()
    {
        var (model, host) = await OpenedAsync();
        await using var _ = model;
        host.Missing = true;
        var path = Snapshots.Path("gone.txt");

        model.Files.OpenCommand.Execute(path);
        Assert.Equal(StatusText.NotInWorkingTree("gone.txt"), model.StatusBar.Notice);
        model.StatusBar.Notice = "";
        model.Files.RevealCommand.Execute(path);
        Assert.Equal(StatusText.NotInWorkingTree("gone.txt"), model.StatusBar.Notice);
    }

    // A program would run, even when asked for directly; a name that is not UTF-8 has no path the machine and Git agree on.
    [Fact]
    public async Task WhenTheFileWouldRunOrItsNameIsNotUtf8_ShouldOfferOnlyWhatIsSafe()
    {
        var (model, host) = await OpenedAsync();
        await using var __ = model;
        var (program, latin) = (Snapshots.Path("setup.exe"), new GitPath([0x63, 0xE8, 0x2E, 0x74]));

        Assert.Equal((false, true, true), (model.Files.OpenCommand.CanExecute(program), model.Files.RevealCommand.CanExecute(program), model.Files.CopyPathCommand.CanExecute(program)));
        Assert.Equal((false, false, false), (model.Files.OpenCommand.CanExecute(latin), model.Files.RevealCommand.CanExecute(latin), model.Files.CopyPathCommand.CanExecute(latin)));
        model.Files.OpenCommand.Execute(program);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task WhenAnUntrackedFileIsIgnored_ShouldSendItsLineToTheWriter()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.False(model.Files.IgnoreCommand.CanExecute(null));
        await model.Files.IgnoreCommand.ExecuteAsync(null);
        await model.Files.IgnoreCommand.ExecuteAsync(IgnoreChoices.Extension(Snapshots.Path("logs/out.log")));

        var sent = Assert.IsType<IgnoreRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("logs/out.log", "*.log"), (sent.Path.Display, sent.Pattern));
    }

    [Fact]
    public async Task WhenAnotherWriteRuns_ShouldNotIgnoreRestoreOrRevertUntilItEnds()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        var choice = IgnoreChoices.File(Snapshots.Path("out.log"));
        var (offers, restoreOffers, revertOffers) = (0, 0, 0);
        model.Files.IgnoreCommand.CanExecuteChanged += (_, _) => offers++;
        model.Files.RequestRestoreCommand.CanExecuteChanged += (_, _) => restoreOffers++;
        model.Files.RevertFileCommand.CanExecuteChanged += (_, _) => revertOffers++;

        h.Writer.Gate.IsClosed = true;
        var other = model.Repository.Writer.RunAsync(expected => new StashRequest(expected, new("keep"), StashAction.Push));
        await h.Writer.Gate.Started;
        var offeredWhileWriting = (model.Files.IgnoreCommand.CanExecute(choice), model.Files.RequestRestoreCommand.CanExecute(CommitFile("a.txt")),
            model.Files.RevertFileCommand.CanExecute(CommitFile("a.txt")));
        h.Writer.Gate.Open(); await other;

        Assert.Equal((false, false, false), offeredWhileWriting);
        Assert.True(model.Files.RevertFileCommand.CanExecute(CommitFile("a.txt")));
        Assert.True(model.Files.IgnoreCommand.CanExecute(choice));
        Assert.True(model.Files.RequestRestoreCommand.CanExecute(CommitFile("a.txt")));
        Assert.Equal((false, false, false), (offers == 0, restoreOffers == 0, revertOffers == 0));
    }

    // A clean file is restored at once: what it had is in HEAD and comes back by Discard; so is a staged one, whose index
    // stays. A file with changes in the working tree asks first, because they would go.
    [Fact]
    public async Task WhenACommitsFileIsRestored_ShouldAskFirstOnlyWhenTheFileHasChanges()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("changed.txt"), Snapshots.Staged("staged.txt")]);
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");

        await model.Files.RequestRestoreCommand.ExecuteAsync(CommitFile("clean.txt"));
        await model.Files.RequestRestoreCommand.ExecuteAsync(CommitFile("staged.txt"));
        var restored = Assert.IsType<RestoreFileRequest>(h.Writer.Requests[0]);
        Assert.Equal((Snapshots.Id('c'), "clean.txt", false), (restored.Commit, restored.Path.Display, model.Files.Restore.IsOpen));
        Assert.Equal("staged.txt", Assert.IsType<RestoreFileRequest>(h.Writer.Requests[1]).Path.Display);

        await model.Files.RequestRestoreCommand.ExecuteAsync(CommitFile("changed.txt"));
        Assert.Equal(2, h.Writer.Requests.Count);
        Assert.Equal(FileVersionText.RestoreQuestion("changed.txt", Snapshots.Id('c')), model.Files.Restore.Text);
        await model.Files.Restore.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("changed.txt", Assert.IsType<RestoreFileRequest>(h.Writer.Requests[^1]).Path.Display);
        Assert.False(model.Files.Restore.IsOpen);
    }

    // What a commit did to a file is undone by the commit and both names of the file, and nothing is asked: the file's own
    // changes stay. A submodule's entry has no lines to undo.
    [Fact]
    public async Task WhenACommitsChangesToAFileAreUndone_ShouldSendTheCommitAndBothNames()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        var renamed = new CommitFileRow(CommitFile("b.txt", "R").File with { OriginalPath = Snapshots.Path("a.txt") }, Snapshots.Id('c'));
        var submodule = new CommitFileRow(CommitFile("lib").File with { OldMode = "160000", NewMode = "160000" }, Snapshots.Id('c'));

        Assert.Equal((false, false), (model.Files.RevertFileCommand.CanExecute(submodule), model.Files.RevertFileCommand.CanExecute(null)));
        await model.Files.RevertFileCommand.ExecuteAsync(submodule);
        await model.Files.RevertFileCommand.ExecuteAsync(renamed);

        var sent = Assert.IsType<RevertFileRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal((Snapshots.Id('c'), "b.txt", "a.txt"), (sent.Commit, sent.Path.Display, sent.OriginalPath?.Display));
    }

    // A name that is not UTF-8 cannot reach Git as text, so its version is neither saved nor restored.
    [Fact]
    public async Task WhenTheCommitLeavesNoFile_ShouldNotSaveOrRestoreIt()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        model.Host = new RecordingHost { SaveAs = @"D:\saved\gone.txt" };
        var latin = new CommitFileRow(CommitFile("x").File with { Path = new GitPath([0x63, 0xE8, 0x2E, 0x74]) }, Snapshots.Id('c'));

        Assert.Equal((false, false), (model.Files.RequestRestoreCommand.CanExecute(CommitFile("gone.txt", "D")), model.Files.RequestRestoreCommand.CanExecute(null)));
        Assert.Equal((false, false, false), (model.Files.SaveVersionCommand.CanExecute(CommitFile("gone.txt", "D")), model.Files.SaveVersionCommand.CanExecute(latin),
            model.Files.RequestRestoreCommand.CanExecute(latin)));
        await model.Files.RequestRestoreCommand.ExecuteAsync(CommitFile("gone.txt", "D"));
        await model.Files.SaveVersionCommand.ExecuteAsync(CommitFile("gone.txt", "D"));
        Assert.Empty(h.Writer.Requests);
        Assert.Empty(h.History.Saves);
    }

    // The version goes where the window's Save dialog says, offered under the file's own name, and the status bar says
    // where; a dialog closed without a choice saves nothing, and a save that fails says so.
    [Fact]
    public async Task WhenACommitsVersionIsSaved_ShouldAskWhereAndSayWhereItWent()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        var host = new RecordingHost { SaveAs = @"D:\saved\a b.txt" };
        model.Host = host;

        await model.Files.SaveVersionCommand.ExecuteAsync(CommitFile("src/a b.txt"));

        Assert.Equal([("save", "a b.txt")], host.Calls);
        Assert.Equal([(Snapshots.Id('c'), "src/a b.txt", @"D:\saved\a b.txt")], h.History.Saves);
        Assert.Equal(FileVersionText.Saved("src/a b.txt", GitObjectId.Short(Snapshots.Id('c')), @"D:\saved\a b.txt"), model.StatusBar.Notice);

        host.SaveAs = null;
        await model.Files.SaveVersionCommand.ExecuteAsync(CommitFile("b.txt"));
        Assert.Single(h.History.Saves);

        (host.SaveAs, h.History.SaveFailure) = (@"D:\saved\c.txt", new IOException());
        await model.Files.SaveVersionCommand.ExecuteAsync(CommitFile("c.txt"));
        Assert.Equal(FileVersionText.NotSaved("c.txt"), model.StatusBar.Notice);
    }

}
