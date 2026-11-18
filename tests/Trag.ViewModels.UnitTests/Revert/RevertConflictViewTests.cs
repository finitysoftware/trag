using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Revert;

// A revert stopped at its conflicts: the revert view opens on them, Finish waits for the last one, Abort asks first,
// and once the revert is over the view gives way to the file status.
public sealed class RevertConflictViewTests
{
    private static readonly string Reverted = Snapshots.Id('b');

    private static RepositorySnapshot Stopped(params FileStatus[] files) =>
        Snapshots.Create(operation: RepositoryOperation.Revert, files: files) with { PickHead = Reverted };

    private static ShellHarness Harness(params FileStatus[] files)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Stopped(files);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    [Fact]
    public async Task WhenARevertStoppedAtConflicts_ShouldOpenTheRevertViewAndHoldFinishUntilTheLastIsResolved()
    {
        var h = Harness(Snapshots.Conflict("a.txt"));
        await using var model = h.Shell();

        await model.Session.OpenRepositoryAsync("fixture");

        Assert.True(model.Repository.HasPick);
        Assert.Equal(WorkspaceSection.Pick, model.Navigator.Section);
        var page = Assert.IsType<PickPage>(model.CurrentSection);
        Assert.Same(model.Pick, page.Pick);
        Assert.Equal("Revert in progress", model.Pick.Words.Title);
        Assert.Equal("1 file is in conflict. Resolve each, then finish the revert.", page.Conflicts.Resolution.Scope.ViewLine);
        Assert.False(model.Pick.FinishPickCommand.CanExecute(null));

        h.Reader.Snapshot = Stopped(Snapshots.Staged("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Pick.FinishPickCommand.CanExecute(null));
        await model.Pick.FinishPickCommand.ExecuteAsync(null);
        Assert.IsType<PickContinueRequest>(Assert.Single(h.Writer.Requests));
    }

    // A file resolved with its markers still in it holds Finish back, as in the merge view, and the view says so.
    [Fact]
    public async Task WhenAResolvedFileStillHoldsMarkers_ShouldHoldFinishAndSaySo()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Stopped(Snapshots.Staged("a.txt")) with
        {
            Resolutions = new([new(Snapshots.Path("a.txt"), Resolution.Edited, HasMarkers: true)], [])
        };
        await using var model = h.Shell();

        await model.Session.OpenRepositoryAsync("fixture");

        Assert.True(model.Resolution.HasMarkers);
        Assert.False(model.Pick.FinishPickCommand.CanExecute(null));
    }

    // A cherry-pick stopped at conflicts takes the same view, named for the cherry-pick.
    [Fact]
    public async Task WhenACherryPickStoppedAtConflicts_ShouldOpenThePickViewNamedForTheCherryPick()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.CherryPick, files: [Snapshots.Conflict("a.txt")]) with { PickHead = Reverted };
        await using var model = h.Shell();

        await model.Session.OpenRepositoryAsync("fixture");

        Assert.True(model.Repository.HasPick);
        Assert.Equal(WorkspaceSection.Pick, model.Navigator.Section);
        Assert.Equal("Cherry-pick in progress", model.Pick.Words.Title);
        Assert.Equal("1 file is in conflict. Resolve each, then finish the cherry-pick.", model.Resolution.Scope.ViewLine);
    }

    // A file already resolved shows its staged diff beside the lists, as in the merge view.
    [Fact]
    public async Task WhenAResolvedFileIsChosenInTheRevertView_ShouldShowItsDiff()
    {
        var h = Harness(Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.Resolution.SelectedResolved = model.Resolution.Resolved.Single(row => row.PathText == "b.txt");
        await model.WorkingDiff.Diff.Completion;

        Assert.Equal("b.txt", model.FileStatus.SelectedStatus?.PathText);
        Assert.Equal(["+staged"], model.WorkingDiff.Diff.Document.Lines.Select(line => line.Text));
    }

    // Going back to the open repository, as Back from the repositories or the commit shortcut do, goes back to the revert.
    [Fact]
    public async Task WhenTheOpenRepositoryIsShownAgainDuringTheRevert_ShouldShowTheRevertView()
    {
        var h = Harness(Snapshots.Conflict("a.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);

        model.PrepareCommitCommand.Execute(null);

        Assert.Equal(WorkspaceSection.Pick, model.Navigator.Section);
    }

    // The abort question belongs to the pick in progress: a snapshot of the same pick keeps it open, one where the pick
    // has ended closes it.
    [Fact]
    public async Task WhenTheAbortQuestionIsOpenAndTheSnapshotChanges_ShouldCloseItOnlyWhenThePickEnded()
    {
        var h = Harness(Snapshots.Conflict("a.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Pick.RequestAbortPickCommand.Execute(null);

        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Conflict("a.txt"), Snapshots.Modified("b.txt")] };
        await model.RefreshCommand.ExecuteAsync(null);
        var keptOpen = model.Pick.Abort.IsOpen;
        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.True(keptOpen);
        Assert.False(model.Pick.Abort.IsOpen);
        Assert.Empty(h.Writer.Requests);
    }

    [Fact]
    public async Task WhenTheRevertIsAbortedAfterTheQuestion_ShouldAbortItAndLeaveTheViewOnceItIsOver()
    {
        var h = Harness(Snapshots.Conflict("a.txt"));
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Create();
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Revert aborted. main is back where it was."));
        };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.Pick.RequestAbortPickCommand.Execute(null);
        Assert.True(model.Pick.Abort.IsOpen);
        await model.Pick.Abort.ConfirmCommand.ExecuteAsync(null);

        Assert.IsType<PickAbortRequest>(Assert.Single(h.Writer.Requests));
        Assert.False(model.Pick.Abort.IsOpen);
        Assert.False(model.Repository.HasPick);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        model.Navigator.ShowPickCommand.Execute(null);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
    }

    // The revert started from History stops at a conflict: the revert view opens on it from there.
    [Fact]
    public async Task WhenARevertStartedHereStopsAtConflicts_ShouldOpenTheRevertView()
    {
        var h = new ShellHarness();
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Stopped(Snapshots.Conflict("a.txt"));
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.NeedsResolution, "Reverting bbbbbbb stopped at 1 conflicted file."));
        };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);

        await model.Revert.Question.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(WorkspaceSection.Pick, model.Navigator.Section);
    }
}
