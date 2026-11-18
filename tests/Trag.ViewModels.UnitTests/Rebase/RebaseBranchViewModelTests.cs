using Trag.Domain.Presentation;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Rebase;

// The merge dialog's two sides over the real workspace and the fake Git: the toolbar opens it on Merge, the switch hands
// the chosen branch to the rebase side and back, what a rebase writes, stops on and leaves behind, and the other way
// round offered for the branch others build on.
public sealed class RebaseBranchViewModelTests
{
    private static readonly string FeatureHead = Snapshots.Id('b');

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = FeatureHead;
        h.Branches.Preview = (_, from) => new(Snapshots.HeadId, from.EndsWith("feature") ? FeatureHead : Snapshots.HeadId, [], 2, 1, []);
        h.Branches.RebasePreview = onto => new(Snapshots.HeadId, onto.EndsWith("feature") ? FeatureHead : Snapshots.HeadId,
            [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 4, 2, [], null, 0);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Rebased main onto feature.");
        return h;
    }

    // Opens the dialog from the toolbar, chooses feature on the merge side and switches to Rebase.
    private static async Task<ShellViewModel> OpenRebase(ShellHarness h)
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Merge.Preview.Completion;
        await model.MergeDialog.ShowRebaseCommand.ExecuteAsync(null);
        await model.MergeDialog.Rebase.Preview.Completion;
        return model;
    }

    // A rebase starts from a branch with commits: over a detached HEAD the rebase side stays shut.
    [Fact]
    public async Task WhenHeadIsDetached_ShouldNotOpenTheRebaseSide()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(branch: null);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.MergeDialog.Rebase.OpenForAsync(null);
        Assert.False(model.MergeDialog.Rebase.Draft.IsOpen);
    }

    [Fact]
    public async Task WhenTheDialogSwitchesBetweenMergeAndRebase_ShouldHandTheBranchAcrossAndAlwaysReopenOnMerge()
    {
        var h = Harness();
        await using var model = await OpenRebase(h);
        var dialog = model.MergeDialog;
        Assert.True(dialog.IsRebase); Assert.True(dialog.IsOpen);
        Assert.Equal(("Rebase a branch", "rebase"), (dialog.Title, dialog.Icon));
        Assert.False(model.MergeDialog.Merge.Draft.IsOpen);
        Assert.Equal(("main", "feature"), (model.MergeDialog.Rebase.Draft.Branch!.Name, model.MergeDialog.Rebase.Draft.Onto!.Name));
        Assert.Equal("refs/heads/feature", Assert.Single(h.Branches.RebasePreviews));
        Assert.Equal("Rebase main onto feature", model.MergeDialog.Rebase.Summary.Action);
        Assert.False(dialog.OpenCommand.CanExecute(null));

        await dialog.ShowMergeCommand.ExecuteAsync(null);
        await model.MergeDialog.Merge.Preview.Completion;
        Assert.False(dialog.IsRebase); Assert.True(dialog.IsOpen);
        Assert.False(model.MergeDialog.Rebase.Draft.IsOpen);
        Assert.Equal(("feature", "main"), (model.MergeDialog.Merge.Draft.From!.Name, model.MergeDialog.Merge.Draft.Into!.Name));

        await dialog.ShowRebaseCommand.ExecuteAsync(null);
        dialog.CancelCommand.Execute(null);
        Assert.False(dialog.IsOpen); Assert.False(model.MergeDialog.Rebase.Draft.IsOpen);
        Assert.True(dialog.OpenCommand.CanExecute(null));
        await dialog.OpenCommand.ExecuteAsync(null);
        Assert.False(dialog.IsRebase); Assert.True(model.MergeDialog.Merge.Draft.IsOpen);
        Assert.Equal(("Merge branches", "merge"), (dialog.Title, dialog.Icon));
    }

    [Fact]
    public async Task WhenTheRebaseRuns_ShouldWriteTheBranchAndTheCommitItWasPreviewedAgainstThenClose()
    {
        var h = Harness();
        await using var model = await OpenRebase(h);
        var rebase = model.MergeDialog.Rebase;
        Assert.True(rebase.RebaseCommand.CanExecute(null));
        await rebase.RebaseCommand.ExecuteAsync(null);
        var request = Assert.IsType<RebaseRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("feature", FeatureHead), (request.OntoName, request.OntoHead));
        Assert.False(model.MergeDialog.IsOpen);
        Assert.Equal(RebasePreview.None, rebase.Preview.Value);
        Assert.Equal("Rebased main onto feature.", model.StatusBar.Status);
    }

    [Fact]
    public async Task WhenTheRebaseStopsAtAConflict_ShouldCloseTheDialogAndOpenTheRebaseView()
    {
        var h = Harness();
        await using var model = await OpenRebase(h);
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Rebasing(Snapshots.Rebase(step: 2, total: 4, branch: "main", ontoName: "feature"), [Snapshots.Conflict("a.txt")]);
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.NeedsResolution, "The rebase stopped at a conflict."));
        };
        await model.MergeDialog.Rebase.RebaseCommand.ExecuteAsync(null);
        Assert.False(model.MergeDialog.IsOpen);
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
    }

    [Fact]
    public async Task WhenGitRejectsTheRebase_ShouldKeepTheDialogOpenWithGitsReason()
    {
        var h = Harness();
        await using var model = await OpenRebase(h);
        h.Writer.Result = GitWriteResult.Rejected("The rebase was rejected. The branch is unchanged.");
        await model.MergeDialog.Rebase.RebaseCommand.ExecuteAsync(null);
        Assert.True(model.MergeDialog.IsOpen); Assert.True(model.MergeDialog.IsRebase);
        Assert.Equal("The rebase was rejected. The branch is unchanged.", model.MergeDialog.Rebase.Draft.Notice);
        Assert.False(model.MergeDialog.Rebase.IsRebasing);
    }

    [Fact]
    public async Task WhenTheBranchOthersBuildOnWouldBeRebased_ShouldOfferTheOtherWayRoundBySwitchingFirst()
    {
        var h = Harness();
        h.Branches.RebasePreview = onto => new(Snapshots.HeadId, onto.EndsWith("feature") ? FeatureHead : Snapshots.HeadId,
            [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 40, 1, [], "origin/main", 40) { UpstreamIsDefault = onto.EndsWith("feature") };
        h.Writer.Handle = (request, _) =>
        {
            if (request is BranchRequest { Action: BranchAction.Switch } switched)
            {
                h.Reader.Snapshot = h.Reader.Snapshot with { Branch = switched.Branch.Name };
                h.Branches.Current = switched.Branch.Name;
            }
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Switched to feature."));
        };
        await using var model = await OpenRebase(h);
        var rebase = model.MergeDialog.Rebase;
        Assert.True(rebase.Summary.ShowsMainline);
        Assert.Equal("Rebase feature onto main instead", rebase.Summary.OtherWayAction);
        Assert.True(rebase.RebaseOtherWayCommand.CanExecute(null));

        await rebase.RebaseOtherWayCommand.ExecuteAsync(null);
        await rebase.Preview.Completion;
        var request = Assert.IsType<BranchRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("feature", BranchAction.Switch), (request.Branch.Name, request.Action));
        Assert.True(model.MergeDialog.IsOpen); Assert.True(model.MergeDialog.IsRebase);
        Assert.Equal(("feature", "main"), (rebase.Draft.Branch!.Name, rebase.Draft.Onto!.Name));
        Assert.Equal("refs/heads/main", h.Branches.RebasePreviews[^1]);
        Assert.False(rebase.Summary.ShowsMainline);
        Assert.Equal("Rebase feature onto main", rebase.Summary.Action);
    }

    [Fact]
    public async Task WhenTheTreeHasUncommittedChanges_ShouldBlockTheRebaseUntilTheyAreGone()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = await OpenRebase(h);
        var rebase = model.MergeDialog.Rebase;
        Assert.True(rebase.Summary.IsBlocked); Assert.False(rebase.RebaseCommand.CanExecute(null));
        Assert.Equal("1 uncommitted file", rebase.Summary.BlockedShort);

        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        await rebase.Preview.Completion;
        Assert.True(model.MergeDialog.IsRebase);
        Assert.False(rebase.Summary.IsBlocked); Assert.True(rebase.RebaseCommand.CanExecute(null));
    }
}
