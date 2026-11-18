using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.History;

// One file's history in the History section: only its commits, each commit's details open on the file, and the whole
// history back once the filter is cleared, History is chosen again or the section is left.
public sealed class FileHistoryViewModelTests
{
    private static FileDiffMetadata File(string path) => new(Snapshots.Path(path), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        return h;
    }

    [Fact]
    public async Task WhenAFilesHistoryIsShown_ShouldAskForItsCommitsUntilItIsCleared()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.History.ShowFileHistory("src/a.txt");
        await model.History.HistoryCompletion;

        Assert.Equal((WorkspaceSection.History, "src/a.txt", "src/a.txt"), (model.Navigator.Section, model.History.FilePath, h.History.LastPath));
        Assert.False(model.History.ShowsGraph); Assert.Equal("History", model.History.Title);
        model.History.ClearFileHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        Assert.Equal((null, null), (model.History.FilePath, h.History.LastPath));
        Assert.True(model.History.ShowsGraph);
    }

    [Fact]
    public async Task WhenHistoryIsChosenAgainOrLeft_ShouldShowTheWholeHistory()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        model.History.ShowFileHistory("a.txt");
        await model.History.HistoryCompletion;
        Assert.Equal("a.txt", h.History.LastPath);

        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        Assert.Equal((null, null), (model.History.FilePath, h.History.LastPath));

        model.History.ShowFileHistory("a.txt");
        model.Navigator.ShowFileStatusCommand.Execute(null);
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        Assert.Equal((null, null), (model.History.FilePath, h.History.LastPath));
    }

    // The file is chosen by the path it has in that commit, the old name before a rename, and its diff is read.
    [Fact]
    public async Task WhenACommitOfAFilesHistoryIsChosen_ShouldOpenItsDetailsOnTheFile()
    {
        var h = Harness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Change a") with { Path = Snapshots.Path("old a.txt") }], false, page);
        h.History.Details = id => new(Snapshots.Commit(id, "Change a"), "Body", [File("b.txt"), File("old a.txt")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.History.ShowFileHistory("a.txt");
        await model.History.HistoryCompletion;

        model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;
        await model.History.Details.Diff.Completion;

        Assert.Equal("old a.txt", model.History.Details.Panel.SelectedFile!.File.Path.Display);
        Assert.Equal("+old a.txt", Assert.Single(model.History.Details.Diff.Document.Presentation.Lines).Text);
    }
}
