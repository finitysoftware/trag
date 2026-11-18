using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Rebase;

// The rebase view over a stopped rebase: the message of each stop, Continue waiting for the stop's conflicts, the sides
// written in Git's terms, Skip, Abort behind its question, and what the file status and History do meanwhile.
public sealed class RebaseViewModelTests
{
    private static RepositorySnapshot Stopped(int step, MergeResolutions resolutions, params FileStatus[] files) =>
        Snapshots.Rebasing(Snapshots.Rebase(step: step, total: 3), files) with { Resolutions = resolutions };

    private static readonly MergeResolutions OneOpen = new([], [new(Snapshots.Path("f.cs"), 1)]);
    private static readonly MergeResolutions Decided = new([new(Snapshots.Path("f.cs"), Resolution.Theirs)], []);

    private static ShellHarness Harness(RepositorySnapshot snapshot)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = snapshot;
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    [Fact]
    public async Task WhenTheRebaseStops_ShouldOpenTheRebaseViewAndContinueWithTheMessageOnceTheConflictsAreDecided()
    {
        var h = Harness(Stopped(1, OneOpen, Snapshots.Conflict("f.cs")));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
        Assert.Same(model.RebasePage, model.CurrentSection);
        Assert.Equal("Commit 1", model.Rebase.Message);
        Assert.Equal("Conflict · 1 block left · yours = commit 1 of 3 · upstream = main", model.Resolution.DetailLine);
        Assert.False(model.Rebase.ContinueRebaseCommand.CanExecute(null));

        // Yours is the replayed commit, which Git calls theirs.
        await model.Resolution.UseYoursCommand.ExecuteAsync(null);
        Assert.Equal(ConflictChoice.Theirs, Assert.IsType<ConflictSideRequest>(h.Writer.LastRequest).Side);

        model.Rebase.Message = "Commit 1, reworded";
        h.Reader.Snapshot = Stopped(1, Decided, Snapshots.Staged("f.cs"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Commit 1, reworded", model.Rebase.Message);
        Assert.True(model.Rebase.ContinueRebaseCommand.CanExecute(null));
        await model.Rebase.ContinueRebaseCommand.ExecuteAsync(null);
        Assert.Equal("Commit 1, reworded", Assert.IsType<RebaseContinueRequest>(h.Writer.LastRequest).Message);

        // The next stop brings its own message and closes an abort question asked at the stop before.
        model.Rebase.RequestAbortRebaseCommand.Execute(null); Assert.True(model.Rebase.Abort.IsOpen);
        h.Reader.Snapshot = Stopped(2, OneOpen, Snapshots.Conflict("f.cs"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Commit 2", model.Rebase.Message); Assert.False(model.Rebase.Abort.IsOpen);
    }

    // An edit stop has no conflict to wait for: the view says it stopped for an edit, and Continue is open at once.
    [Fact]
    public async Task WhenTheRebaseStoppedForEdit_ShouldSaySoAndLetItContinue()
    {
        var h = Harness(Snapshots.Rebasing(Snapshots.Rebase(step: 1, total: 3) with { StoppedForEdit = true }));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.EndsWith(" stopped so you can edit it. Continue replays the rest.", model.Rebase.Overview.Remaining);
        Assert.True(model.Rebase.ContinueRebaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheStopIsSkippedOrAborted_ShouldSkipAtOnceAndAbortOnlyAfterTheQuestion()
    {
        var h = Harness(Stopped(2, OneOpen, Snapshots.Conflict("f.cs")));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Rebase.SkipCommitCommand.ExecuteAsync(null);
        Assert.IsType<RebaseSkipRequest>(Assert.Single(h.Writer.Requests));

        model.Rebase.RequestAbortRebaseCommand.Execute(null);
        Assert.True(model.Rebase.Abort.IsOpen); Assert.Single(h.Writer.Requests);
        model.Rebase.Abort.CancelCommand.Execute(null);
        Assert.False(model.Rebase.Abort.IsOpen);
        model.Rebase.RequestAbortRebaseCommand.Execute(null);
        await model.Rebase.Abort.ConfirmCommand.ExecuteAsync(null);
        Assert.IsType<RebaseAbortRequest>(h.Writer.LastRequest);
        Assert.False(model.Rebase.Abort.IsOpen);
    }

    // Git may refuse the abort: the question stays while Git runs and then says why, with the rebase still there to keep.
    [Fact]
    public async Task WhenTheAbortRunsOrGitRefusesIt_ShouldKeepTheQuestionOpenAndShowGitsReasonInIt()
    {
        var h = Harness(Stopped(2, OneOpen, Snapshots.Conflict("f.cs")));
        h.Writer.Result = GitWriteResult.Rejected("Git could not abort.");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Rebase.RequestAbortRebaseCommand.Execute(null);
        Assert.StartsWith("Git puts the branch back where it was before the rebase.", model.Rebase.Abort.Text);

        h.Writer.Gate.IsClosed = true;
        var pending = model.Rebase.Abort.ConfirmCommand.ExecuteAsync(null); await h.Writer.Gate.Started;
        Assert.True(model.Rebase.Abort.IsOpen); Assert.True(model.Rebase.Abort.IsRunning);
        h.Writer.Gate.Open(); await pending;

        Assert.IsType<RebaseAbortRequest>(h.Writer.LastRequest);
        Assert.True(model.Rebase.Abort.IsOpen); Assert.Equal("Git could not abort.", model.Rebase.Abort.Message);
    }

    [Fact]
    public async Task WhenTheFileStatusShowsAStoppedRebase_ShouldStageAResolutionAndSendConflictsToTheRebaseView()
    {
        var h = Harness(StoppedWithNotes());
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowFileStatusCommand.Execute(null);
        Assert.True(model.Resolution.Scope.ShowsBanner);
        Assert.Equal("Open rebase view", model.FileStatusPage.Resolution.Scope.OpenText);
        Assert.StartsWith("A rebase is in progress.", model.Commit.UnstageTip);

        model.FileStatus.SelectedUnstaged = (Trag.ViewModels.Status.StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.Commit.StageSelectedCommand.ExecuteAsync(null);
        Assert.True(Assert.IsType<StageRequest>(h.Writer.LastRequest).Resolution);

        model.Resolution.ShowConflictCommand.Execute(model.Resolution.Conflicts[0]);
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
    }

    // The double-click follows a resolution staged from the file status and a jump to the conflict.
    [Fact]
    public async Task WhenACommitIsDoubleClickedInHistoryDuringARebase_ShouldOpenTheDialogBlockedWithTheWayToTheRebaseView()
    {
        var h = Harness(StoppedWithNotes());
        await using var model = h.Shell();
        await StageThenShowTheConflictAsync(model);

        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = Snapshots.Commit(Snapshots.Id('b'), "Older");
        await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.Dialog.IsBlockedByOperation);
        Assert.Equal("Open rebase view", model.GoTo.Dialog.OperationViewText);
        model.GoTo.OpenOperationViewCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
    }

    // The first stop with its conflict and an unrelated unstaged change.
    private static RepositorySnapshot StoppedWithNotes() => Stopped(1, OneOpen, Snapshots.Conflict("f.cs"), Snapshots.Modified("notes.md"));

    // Opens the stop, stages the unstaged file from the file status and goes to the conflict in the rebase view.
    private static async Task StageThenShowTheConflictAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowFileStatusCommand.Execute(null);
        model.FileStatus.SelectedUnstaged = (Trag.ViewModels.Status.StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.Commit.StageSelectedCommand.ExecuteAsync(null);
        model.Resolution.ShowConflictCommand.Execute(model.Resolution.Conflicts[0]);
    }
}
