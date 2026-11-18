using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// When a merge can start at all, when its sides can be swapped, and what the preview says about the merge commit.
public sealed class MergeChoiceTests
{
    private static readonly BranchInfo Main = new("main", "refs/heads/main", Snapshots.HeadId, false);
    private static readonly BranchInfo Feature = new("feature", "refs/heads/feature", Snapshots.Id('b'), false);
    private static readonly BranchInfo Develop = new("origin/develop", "refs/remotes/origin/develop", Snapshots.Id('c'), true, "origin");

    [Fact]
    public void WhenTheMergeGoesIntoTheCurrentBranch_ShouldNotSwitchBranch()
    {
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.False(choice.SwitchesBranch);
    }

    [Fact]
    public void WhenTheMergeGoesIntoTheCurrentBranch_ShouldHandTheBranchItTakesFromToTheRebase() =>
        Assert.Same(Feature, new MergeChoice(Feature, Main, "main").RebaseOnto);

    [Fact]
    public void WhenTheMergeGoesIntoAnotherBranch_ShouldHandNothingToTheRebase() =>
        Assert.Null(new MergeChoice(Main, Feature, "main").RebaseOnto);

    [Fact]
    public void WhenNoTargetIsChosen_ShouldHandNothingToTheRebase() =>
        Assert.Null(new MergeChoice(Feature, null, null).RebaseOnto);

    [Fact]
    public void WhenTheCommitsComeFromARemoteBranch_ShouldNotSwap()
    {
        var remote = new MergeChoice(Develop, Main, "main");
        Assert.False(remote.CanSwap);
    }

    [Fact]
    public void WhenNoBranchGivesTheCommits_ShouldNotBeComplete()
    {
        Assert.False(new MergeChoice(null, Main, "main").IsComplete);
    }

    [Fact]
    public void WhenTheRepositoryIsOnABranchWithACommit_ShouldLetAMergeStart()
    {
        Assert.True(CanStart(Snapshots.Create()));
    }

    [Fact]
    public void WhenNoRepositoryIsRead_ShouldNotLetAMergeStart()
    {
        Assert.False(CanStart(null));
    }

    [Fact]
    public void WhenHeadIsDetached_ShouldNotLetAMergeStart()
    {
        Assert.False(CanStart(Snapshots.Create(branch: null)));
    }

    [Fact]
    public void WhenTheBranchHasNoCommitYet_ShouldNotLetAMergeStart()
    {
        Assert.False(CanStart(Snapshots.Create(unborn: true)));
    }

    [Fact]
    public void WhenAnOperationIsInProgress_ShouldNotLetAMergeStart()
    {
        Assert.False(CanStart(Snapshots.Create(operation: RepositoryOperation.Merge)));
    }

    [Fact]
    public void WhenTheWorkingTreeHasChanges_ShouldStillLetAMergeStart()
    {
        Assert.True(CanStart(Snapshots.Create(files: [Snapshots.Modified("a.txt")])));
    }

    [Fact]
    public void WhenOnlyTheIncomingBranchHasNewCommits_ShouldFastForwardWithoutAMergeCommit()
    {
        var ahead = Ahead();
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.True(ahead.CanFastForward); Assert.False(choice.CreatesMergeCommit(ahead));
    }

    [Fact]
    public void WhenTheUserAsksForAMergeCommit_ShouldCreateOneThoughGitCouldFastForward()
    {
        var ahead = Ahead();
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.True((choice with { NoFastForward = true }).CreatesMergeCommit(ahead));
    }

    [Fact]
    public void WhenBothBranchesHaveNewCommits_ShouldCreateAMergeCommit()
    {
        var diverged = Ahead() with { OwnCount = 2 };
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.True(choice.CreatesMergeCommit(diverged)); Assert.False(diverged.CanFastForward);
    }

    [Fact]
    public void WhenNothingComesIn_ShouldBeUpToDateAndCreateNoMergeCommit()
    {
        var upToDate = Ahead() with { IncomingCount = 0 };
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.True(upToDate.IsUpToDate); Assert.False(choice.CreatesMergeCommit(upToDate));
    }

    [Fact]
    public void WhenGitHasNotAnsweredThePreview_ShouldCreateNoMergeCommit()
    {
        var choice = new MergeChoice(Feature, Main, "main");
        Assert.False(MergePreview.None.IsRead); Assert.False(choice.CreatesMergeCommit(MergePreview.None));
    }

    // The preview lists a few of the incoming commits; the others are only counted.
    [Fact]
    public void WhenMoreCommitsComeInThanThePreviewLists_ShouldCountTheOthers()
    {
        var ahead = Ahead();
        Assert.Equal(3, ahead.MoreCount);
        Assert.Equal(2, (ahead with { IncomingCount = 7, Incoming = [.. Enumerable.Repeat(Snapshots.Commit(Snapshots.HeadId, "x"), 5)] }).MoreCount);
    }

    [Fact]
    public void WhenALocalBranchIsMerged_ShouldUseGitsWordingAndNameTheTarget()
    {
        Assert.Equal("Merge branch 'feature' into main", MergeMessages.Default(Feature, Main));
    }

    [Fact]
    public void WhenARemoteBranchIsMerged_ShouldUseGitsWordingForARemoteTrackingBranchAndNameTheTarget()
    {
        Assert.Equal("Merge remote-tracking branch 'origin/develop' into feature", MergeMessages.Default(Develop, Feature));
    }

    [Fact]
    public void WhenNoBranchGivesTheCommits_ShouldOfferNoMessage()
    {
        Assert.Equal("", MergeMessages.Default(null, Main));
    }

    // Three commits come in and the target has none of its own; the preview lists none of them.
    private static MergePreview Ahead() => new(Snapshots.HeadId, Snapshots.Id('b'), [], 3, 0, []);

    private static bool CanStart(RepositorySnapshot? snapshot) => snapshot is { CanStartOperation: true };
}
