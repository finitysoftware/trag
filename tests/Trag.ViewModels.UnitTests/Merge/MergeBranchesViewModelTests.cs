using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Merge;

// The merge dialog over the real workspace and the fake Git: where it opens, when it reads the preview, what stops it,
// and what a merge into the current or another branch writes.
public sealed class MergeBranchesViewModelTests
{
    private static readonly string FeatureHead = Snapshots.Id('b');

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = FeatureHead;
        h.Branches.Preview = (_, from) => new(Snapshots.HeadId, from.EndsWith("feature") ? FeatureHead : Snapshots.HeadId, [], 2, 1, []);
        // A switch moves HEAD to the branch the way Git would, so the next write starts from there.
        h.Writer.Handle = (request, _) =>
        {
            if (request is BranchRequest { Action: BranchAction.Switch } branch) h.Reader.Snapshot = h.Reader.Snapshot with { Branch = branch.Branch.Name };
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, request is MergeRequest ? "Merged." : "Switched."));
        };
        return h;
    }

    private static async Task<ShellViewModel> Open(ShellHarness h, string? from = "feature")
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.MergeDialog.Merge.OpenCommand.ExecuteAsync(null);
        if (from is not null) model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == from);
        await model.MergeDialog.Merge.Preview.Completion;
        return model;
    }

    // A merge starts from a branch with commits: over a detached HEAD or a repository without commits the dialog stays shut.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenHeadIsDetachedOrHasNoCommits_ShouldNotOpenTheDialog(bool detached)
    {
        var h = Harness();
        h.Reader.Snapshot = detached ? Snapshots.Create(branch: null) : Snapshots.Create(unborn: true);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.False(model.MergeDialog.Merge.OpenCommand.CanExecute(null));
        await model.MergeDialog.Merge.OpenForAsync(null);
        Assert.False(model.MergeDialog.Merge.Draft.IsOpen);
    }

    [Fact]
    public async Task WhenTheDialogOpensFromTheToolbar_ShouldStartOnTheCurrentBranchAndPreviewAndMergeOnceBothSidesAreChosen()
    {
        var h = Harness();
        await using var model = await Open(h, from: null);
        var merge = model.MergeDialog.Merge;
        Assert.True(merge.Draft.IsOpen);
        Assert.Equal(("main", true), (merge.Draft.Into!.Name, merge.Draft.Into.IsCurrent));
        Assert.Null(merge.Draft.From);
        Assert.Equal(["main", "feature"], merge.Draft.FromOptions.Select(row => row.Name));
        Assert.Empty(h.Branches.Previews); Assert.False(merge.MergeCommand.CanExecute(null));

        merge.Draft.From = merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await merge.Preview.Completion;
        Assert.Equal(("refs/heads/main", "refs/heads/feature"), Assert.Single(h.Branches.Previews));
        Assert.Equal("Merge branch 'feature' into main", merge.Draft.Message);
        Assert.Equal(("2 commits main doesn't have", "Merge feature into main"), (merge.Summary.FromHint, merge.Summary.Action));
        Assert.True(merge.MergeCommand.CanExecute(null));

        await merge.MergeCommand.ExecuteAsync(null);
        var request = Assert.IsType<MergeRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("feature", FeatureHead, "Merge branch 'feature' into main"), (request.FromName, request.FromHead, request.Message));
        Assert.False(merge.Draft.IsOpen);
    }

    [Fact]
    public async Task WhenAPickerRowAsksForAMerge_ShouldOpenTheDialogWithThatBranchToMergeFrom()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        model.Branches.IsPickerOpen = true;
        var row = model.Branches.Rows.Local.Single(branch => branch.Name == "feature");
        Assert.True(row.CanMerge); Assert.False(model.Branches.Rows.Current!.CanMerge);
        model.Branches.RequestMergeCommand.Execute(row);
        await model.Branches.List.Completion; await model.MergeDialog.Merge.Preview.Completion;
        Assert.False(model.Branches.IsPickerOpen);
        Assert.Equal(("feature", "main"), (model.MergeDialog.Merge.Draft.From!.Name, model.MergeDialog.Merge.Draft.Into!.Name));
        Assert.Single(h.Branches.Previews);
    }

    [Fact]
    public async Task WhenTheTreeHasUncommittedChanges_ShouldBlockTheMergeUntilTheyAreStashedWhileTheDialogStaysOpen()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = await Open(h);
        var merge = model.MergeDialog.Merge;
        Assert.True(merge.Summary.IsBlocked); Assert.False(merge.MergeCommand.CanExecute(null));
        Assert.Equal("1 uncommitted file", merge.Summary.BlockedShort);

        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(merge.Draft.IsOpen);
        Assert.False(merge.Summary.IsBlocked); Assert.True(merge.MergeCommand.CanExecute(null));
        Assert.Single(h.Branches.Previews);
    }

    // The file status is where the changes that block the merge can be committed or discarded.
    [Fact]
    public async Task WhenTheUserGoesToReviewTheChangesThatBlockTheMerge_ShouldCloseTheDialogAndShowTheFileStatus()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = await Open(h);
        model.Navigator.ShowHistoryCommand.Execute(null);

        model.MergeDialog.Merge.ReviewChangesCommand.Execute(null);

        Assert.False(model.MergeDialog.Merge.Draft.IsOpen); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
    }

    // A branch made elsewhere while the dialog is open becomes a choice once the list reads it.
    [Fact]
    public async Task WhenABranchAppearsWhileTheDialogIsOpen_ShouldOfferIt()
    {
        var h = Harness();
        await using var model = await Open(h);
        h.Branches.Local.Add("hotfix");
        h.Branches.Heads["hotfix"] = Snapshots.Id('c');
        h.Reader.Snapshot = h.Reader.Snapshot with { LocalRefs = "moved" };

        await model.RefreshCommand.ExecuteAsync(null);
        await model.Repository.Branches.List.Completion;

        Assert.Contains(model.MergeDialog.Merge.Draft.FromOptions, row => row.Name == "hotfix");
    }

    // A commit made elsewhere moves the branch the preview was read over: the preview is read again.
    [Fact]
    public async Task WhenTheRepositoryMovesWhileTheDialogIsOpen_ShouldReadThePreviewAgain()
    {
        var h = Harness();
        await using var model = await Open(h);
        h.Reader.Snapshot = h.Reader.Snapshot with { Head = Snapshots.Id('d') };

        await model.RefreshCommand.ExecuteAsync(null);
        await model.MergeDialog.Merge.Preview.Completion;

        Assert.Equal(2, h.Branches.Previews.Count);
    }

    // Closing drops the choices: the dialog opened again starts from the current branch with the defaults.
    [Fact]
    public async Task WhenTheDialogOpensAgainAfterACancel_ShouldStartOverWithTheDefaults()
    {
        var h = Harness();
        await using var model = await Open(h);
        var merge = model.MergeDialog.Merge;
        (merge.Draft.NoFastForward, merge.Draft.StayOnTarget, merge.Draft.PushAfter) = (true, true, true);
        merge.CancelCommand.Execute(null);

        await merge.OpenCommand.ExecuteAsync(null);

        Assert.Null(merge.Draft.From);
        Assert.Equal((false, false, false), (merge.Draft.NoFastForward, merge.Draft.StayOnTarget, merge.Draft.PushAfter));
    }

    [Fact]
    public async Task WhenTheSidesAreSwapped_ShouldMergeTheCurrentBranchIntoTheOtherOneAndSwitchBack()
    {
        var h = Harness();
        await using var model = await Open(h);
        var merge = model.MergeDialog.Merge;
        merge.SwapCommand.Execute(null);
        await merge.Preview.Completion;
        Assert.Equal(("main", "feature"), (merge.Draft.From!.Name, merge.Draft.Into!.Name));
        Assert.Equal("Merge branch 'main' into feature", merge.Draft.Message);
        Assert.Equal("feature is not checked out. Trag switches to feature, merges, then switches back to main.", merge.Summary.SwitchText);

        await merge.MergeCommand.ExecuteAsync(null);
        Assert.Equal(["switch feature", "merge main", "switch main"], h.Writer.Requests.Select(request => request switch
        {
            BranchRequest branch => "switch " + branch.Branch.Name,
            MergeRequest merged => "merge " + merged.FromName,
            _ => "?"
        }));
        Assert.False(merge.Draft.IsOpen);
        Assert.Equal("main", model.Repository.Snapshot!.Branch);
        Assert.Equal("Merged. Back on main.", model.StatusBar.Status);
    }

    // The message shows only for a merge commit. One the user emptied while asking for a merge commit, and then hid by
    // taking the fast-forward again, is not what the merge carries: Git may still have to write a merge commit, so the
    // request needs a message, and the writer refuses one without it only after the switch.
    [Fact]
    public async Task WhenTheUserEmptiesTheMessageAndThenChoosesTheFastForward_ShouldMergeWithTheMessageTragWrote()
    {
        var h = Harness();
        h.Branches.Preview = (_, from) => new(Snapshots.HeadId, from.EndsWith("feature") ? FeatureHead : Snapshots.HeadId, [], 2, 0, []);
        var handle = h.Writer.Handle!;
        h.Writer.Handle = (request, ct) => GitWriteRequestValidator.Validate(request) is { } refused
            ? Task.FromResult(GitWriteResult.Rejected(refused)) : handle(request, ct);
        await using var model = await Open(h);
        var merge = model.MergeDialog.Merge;
        merge.SwapCommand.Execute(null);
        await merge.Preview.Completion;
        merge.Draft.NoFastForward = true;
        merge.Draft.Message = "";
        merge.Draft.NoFastForward = false;
        Assert.False(merge.Summary.CreatesMergeCommit);

        await merge.MergeCommand.ExecuteAsync(null);

        var request = Assert.Single(h.Writer.Requests.OfType<MergeRequest>());
        Assert.Equal("Merge branch 'main' into feature", request.Message);
        Assert.Equal("Merged. Back on main.", model.StatusBar.Status);
    }

    [Fact]
    public async Task WhenGitRejectsTheMerge_ShouldKeepTheDialogOpenWithGitsReason()
    {
        var h = Harness();
        await using var model = await Open(h);
        h.Writer.Handle = null; h.Writer.Result = GitWriteResult.Rejected("Merge was rejected. The branch and working tree are unchanged.");
        await model.MergeDialog.Merge.MergeCommand.ExecuteAsync(null);
        Assert.True(model.MergeDialog.Merge.Draft.IsOpen);
        Assert.Equal("Merge was rejected. The branch and working tree are unchanged.", model.MergeDialog.Merge.Draft.Notice);
        Assert.False(model.MergeDialog.Merge.IsMerging);
    }

    [Fact]
    public async Task WhenTheMergeLeavesConflicts_ShouldCloseTheDialogAndOpenTheMergeView()
    {
        var h = Harness();
        await using var model = await Open(h);
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.Merge, files: [Snapshots.Conflict("a.txt")],
                merge: new(FeatureHead, Snapshots.HeadId, "Merge branch 'feature' into main", "merge", ""));
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.NeedsResolution, "Merge left conflicts to resolve."));
        };
        await model.MergeDialog.Merge.MergeCommand.ExecuteAsync(null);
        Assert.False(model.MergeDialog.Merge.Draft.IsOpen);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
    }

    [Fact]
    public async Task WhenTheSameBranchIsOnBothSides_ShouldNeitherPreviewNorMerge()
    {
        var h = Harness();
        await using var model = await Open(h, from: "main");
        Assert.Empty(h.Branches.Previews);
        Assert.Equal("Choose a branch other than main", model.MergeDialog.Merge.Summary.FromHint);
        Assert.False(model.MergeDialog.Merge.MergeCommand.CanExecute(null));
        Assert.Equal(MergePreview.None, model.MergeDialog.Merge.Preview.Value);
    }
}
