using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Branches;

// Double-click in History with uncommitted changes: the go-to dialog opens blocked, like the merge dialog, offers the
// way to File status, and follows a stash (a clean tree) without being opened again; a branch's tip waits the same way.
public sealed class GoToBlockedTests
{
    private static readonly string Second = Snapshots.Id('b');

    private static ShellHarness Harness(params string[] local)
    {
        var h = new ShellHarness();
        h.Branches.Local.Clear(); h.Branches.Local.AddRange(["main", .. local]);
        foreach (var name in local) h.Branches.Heads[name] = Second;
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Second), Snapshots.Commit(Second, "Initial")], false, page);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt")]);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    private static async Task DoubleClickSecondAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1];
        await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task WhenTheTreeHasChanges_ShouldOpenTheDialogBlockedAndFollowAStashThatCleansIt()
    {
        var h = Harness();
        await using var model = h.Shell();
        await DoubleClickSecondAsync(model);

        Assert.True(model.GoTo.Target.IsOpen); Assert.True(model.GoTo.Dialog.IsBlocked); Assert.False(model.GoTo.Dialog.IsTip);
        Assert.Equal("Go to " + Second[..7], model.GoTo.Dialog.Title);
        Assert.StartsWith("2 files have uncommitted changes.", model.GoTo.Dialog.BlockedText);
        Assert.False(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null)); Assert.False(model.GoTo.DetachAtCommitCommand.CanExecute(null));

        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.Target.IsOpen); Assert.False(model.GoTo.Dialog.IsBlocked);
        Assert.True(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null)); Assert.True(model.GoTo.DetachAtCommitCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    [Fact]
    public async Task WhenReviewInFileStatusIsChosen_ShouldLeaveTheDialogForTheChanges()
    {
        var h = Harness();
        await using var model = h.Shell();
        await DoubleClickSecondAsync(model);
        Assert.Equal(WorkspaceSection.History, model.Navigator.Section);

        model.GoTo.ReviewChangesCommand.Execute(null);
        Assert.False(model.GoTo.Target.IsOpen); Assert.False(model.GoTo.Dialog.IsBlocked);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
    }

    [Fact]
    public async Task WhenABranchTipIsBlockedByChanges_ShouldWaitInTheDialogAndSwitchThereOnceTheTreeIsClean()
    {
        var h = Harness("old");
        await using var model = h.Shell();
        await DoubleClickSecondAsync(model);

        Assert.True(model.GoTo.Dialog.IsTip); Assert.True(model.GoTo.Dialog.IsBlocked);
        Assert.Equal("Switch to old", model.GoTo.Dialog.Title);
        Assert.Equal("This commit is the tip of old, so Trag switches to that branch. You're on main now; it stays where it is.", model.GoTo.Dialog.TipText);
        Assert.False(model.GoTo.SwitchToTipCommand.CanExecute(null)); Assert.False(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null));

        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.SwitchToTipCommand.CanExecute(null)); Assert.False(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null));
        await model.GoTo.SwitchToTipCommand.ExecuteAsync(null);
        var request = Assert.IsType<BranchRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal((BranchAction.Switch, "old"), (request.Action, request.Branch.Name));
        Assert.False(model.GoTo.Target.IsOpen); Assert.False(model.GoTo.Dialog.IsTip);
    }

    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldOpenTheDialogBlockedWithTheWayToTheMergeView()
    {
        var h = Harness("old");
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("app.csproj")], operation: RepositoryOperation.Merge,
            merge: new(Snapshots.Id('c'), Snapshots.HeadId, "Merge remote-tracking branch 'origin/dotnet8.0' into main", "fp", ""));
        await using var model = h.Shell();
        await DoubleClickSecondAsync(model);

        Assert.True(model.GoTo.Target.IsOpen); Assert.True(model.GoTo.Dialog.IsBlockedByOperation);
        Assert.Equal("Finish or abort the merge first", model.GoTo.Dialog.BlockedTitle);
        Assert.StartsWith("A merge of origin/dotnet8.0 into main is still in progress.", model.GoTo.Dialog.BlockedText);
        Assert.False(model.GoTo.SwitchToTipCommand.CanExecute(null));

        model.GoTo.OpenOperationViewCommand.Execute(null);
        Assert.False(model.GoTo.Target.IsOpen); Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        Assert.Empty(h.Writer.Requests);
    }
}
