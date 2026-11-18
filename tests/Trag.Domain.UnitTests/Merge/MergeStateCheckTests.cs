using Trag.Domain.Status;
using Trag.Domain.Merge;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// The questions a merge's verification asks of the repository after Git ran, in order: did Git leave a merge for the user
// to finish, did it fast-forward, did it commit the merge, and did it change nothing at all.
public sealed class MergeStateCheckTests
{
    private static readonly string Incoming = Snapshots.Id('b');
    private static readonly RepositorySnapshot Before = Snapshots.Create();
    private static readonly RepositorySnapshot Pending = Before with
    {
        Operation = RepositoryOperation.Merge,
        Merge = new(Incoming, Before.Head, "Merge branch 'trunk'", "fingerprint", "")
    };

    [Fact]
    public void WhenGitRanToTheEndAndRefusedToCommit_ShouldLeaveTheMergePending()
    {
        var check = new MergeStateCheck(Before, Pending, Incoming);
        Assert.True(check.HasPendingMerge);
        Assert.True(check.LeftPendingMerge(1, false));
    }

    // A merge that Git says it finished is verified by the later questions.
    [Fact]
    public void WhenGitSaysItFinishedTheMerge_ShouldNotLeaveItPending()
    {
        var check = new MergeStateCheck(Before, Pending, Incoming);
        Assert.True(check.HasPendingMerge);
        Assert.False(check.LeftPendingMerge(0, false));
    }

    // A merge that never got an answer is verified by the later questions.
    [Fact]
    public void WhenTheMergeWasCanceledBeforeGitAnswered_ShouldNotLeaveItPending()
    {
        var check = new MergeStateCheck(Before, Pending, Incoming);
        Assert.True(check.HasPendingMerge);
        Assert.False(check.LeftPendingMerge(null, true));
    }

    // An exit code that came with a failure does not show that Git ran to the end; the later questions verify the merge.
    [Fact]
    public void WhenTheRunFailed_ShouldNotLeaveTheMergePending()
    {
        var check = new MergeStateCheck(Before, Pending, Incoming);
        Assert.True(check.HasPendingMerge);
        Assert.False(check.LeftPendingMerge(1, true));
    }

    [Fact]
    public void WhenTheMergeTakesInAnotherCommit_ShouldNotBeThisPullsMerge() =>
        Assert.False(new MergeStateCheck(Before, Pending, Snapshots.Id('c')).HasPendingMerge);

    [Fact]
    public void WhenTheMergeIsOnAnotherBranch_ShouldNotBeThisPullsMerge() =>
        Assert.False(new MergeStateCheck(Before, Pending with { Branch = "other" }, Incoming).HasPendingMerge);

    [Fact]
    public void WhenHeadIsOnAnotherCommit_ShouldNotBeThisPullsMerge() =>
        Assert.False(new MergeStateCheck(Before, Pending with { Head = Snapshots.Id('d') }, Incoming).HasPendingMerge);

    private static readonly RepositorySnapshot FastForwarded = Before with { Head = Incoming };

    [Fact]
    public void WhenHeadMovedToTheIncomingCommitWithItsTree_ShouldBeAFastForward() =>
        Assert.True(new MergeStateCheck(Before, FastForwarded, Incoming).IsFastForward("tree", "tree"));

    [Theory]
    [InlineData("branch")]
    [InlineData("index")]
    [InlineData("head")]
    [InlineData("operation")]
    [InlineData("tree")]
    public void WhenAnythingButAFastForwardIsLeft_ShouldNotBeAFastForward(string differs)
    {
        var actual = differs switch
        {
            "branch" => FastForwarded with { Branch = "other" },
            "index" => FastForwarded with { IndexFingerprint = "changed" },
            "head" => FastForwarded with { Head = Snapshots.Id('d') },
            "operation" => FastForwarded with { Operation = RepositoryOperation.Merge },
            _ => FastForwarded,
        };
        Assert.False(new MergeStateCheck(Before, actual, Incoming).IsFastForward("tree", differs == "tree" ? "incoming" : "tree"));
    }

    private static readonly string MergeCommit = Snapshots.Id('m');
    private static readonly RepositorySnapshot Committed = Before with { Head = MergeCommit };

    [Fact]
    public void WhenHeadMovedOnTheSameBranchWithAnIndexTree_ShouldBeWorthReadingAsAMergeCommit() =>
        Assert.True(new MergeStateCheck(Before, Committed, Incoming).CouldBeMergeCommit("tree"));

    [Theory]
    [InlineData("branch")]
    [InlineData("unborn")]
    [InlineData("unmoved")]
    [InlineData("tree")]
    public void WhenHeadDidNotMoveToANewCommitOrTheIndexHasNoTree_ShouldNotBeReadAsAMergeCommit(string differs)
    {
        var actual = differs switch
        {
            "branch" => Committed with { Branch = "other" },
            "unborn" => Committed with { Head = null },
            "unmoved" => Before,
            _ => Committed,
        };
        Assert.False(new MergeStateCheck(Before, actual, Incoming).CouldBeMergeCommit(differs == "tree" ? "" : "tree"));
    }

    [Fact]
    public void WhenTheCommitHasTheOldHeadAndTheIncomingCommitAsParentsAndTheIndexTree_ShouldBeTheMerge() =>
        Assert.True(new MergeStateCheck(Before, Committed, Incoming).MatchesCommit([Before.Head + " " + Incoming, "tree"], "tree"));

    [Theory]
    [InlineData("count")]
    [InlineData("parents")]
    [InlineData("tree")]
    public void WhenTheCommitIsNotTheMergeOfThoseTwoParentsWithThatTree_ShouldNotBeTheMerge(string differs)
    {
        string[] commit = differs switch
        {
            "count" => [Before.Head + " " + Incoming],
            "parents" => [Incoming + " " + Before.Head, "tree"],
            _ => [Before.Head + " " + Incoming, "other"],
        };
        Assert.False(new MergeStateCheck(Before, Committed, Incoming).MatchesCommit(commit, "tree"));
    }

    // A canceled post-merge hook can leave Git's merge state behind a merge commit that is already HEAD.
    private static readonly RepositorySnapshot CommittedWithState = Committed with
    {
        Operation = RepositoryOperation.Merge,
        Merge = new(Incoming, Before.Head, "Merge branch 'trunk'", "fingerprint", "")
    };

    [Fact]
    public void WhenThisMergesStateIsLeftBehindItsCommit_ShouldNeedItCleanedUp() =>
        Assert.True(new MergeStateCheck(Before, CommittedWithState, Incoming).NeedsMetadataCleanup);

    [Theory]
    [InlineData("operation")]
    [InlineData("limitation")]
    [InlineData("mergeLimitation")]
    [InlineData("incoming")]
    [InlineData("original")]
    public void WhenTheStateLeftBehindIsNotThisMergesAlone_ShouldNotCleanItUp(string differs)
    {
        var merge = CommittedWithState.Merge!;
        var actual = differs switch
        {
            "operation" => CommittedWithState with { Operation = RepositoryOperation.None },
            "limitation" => CommittedWithState with { Limitations = ["sequencer"] },
            "mergeLimitation" => CommittedWithState with { Merge = merge with { Limitation = "octopus" } },
            "incoming" => CommittedWithState with { Merge = merge with { IncomingHead = Snapshots.Id('c') } },
            _ => CommittedWithState with { Merge = merge with { OriginalHead = Snapshots.Id('c') } },
        };
        Assert.False(new MergeStateCheck(Before, actual, Incoming).NeedsMetadataCleanup);
    }

    [Fact]
    public void WhenTheCleanupLeavesTheMergeCommitOnTheBranchWithTheSameIndex_ShouldHaveFinishedTheMerge() =>
        Assert.True(new MergeStateCheck(Before, CommittedWithState, Incoming).HasFinishedMerge(Committed));

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("operation")]
    [InlineData("index")]
    public void WhenTheCleanupLeftAnythingElse_ShouldNotHaveFinishedTheMerge(string differs)
    {
        var finished = differs switch
        {
            "head" => Committed with { Head = Snapshots.Id('d') },
            "branch" => Committed with { Branch = "other" },
            "operation" => Committed with { Operation = RepositoryOperation.Merge },
            _ => Committed with { IndexFingerprint = "changed" },
        };
        Assert.False(new MergeStateCheck(Before, CommittedWithState, Incoming).HasFinishedMerge(finished));
    }

    [Fact]
    public void WhenNothingMovedAndTheIndexStillHoldsTheOriginalTree_ShouldBeUnchanged() =>
        Assert.True(new MergeStateCheck(Before, Before, Incoming).IsUnchanged("tree", "tree"));

    [Theory]
    [InlineData("head")]
    [InlineData("files")]
    [InlineData("tree")]
    public void WhenAnythingMovedOrChanged_ShouldNotBeUnchanged(string differs)
    {
        var actual = differs switch
        {
            "head" => Committed,
            "files" => Before with { Files = [Snapshots.Modified("a")] },
            _ => Before,
        };
        Assert.False(new MergeStateCheck(Before, actual, Incoming).IsUnchanged("tree", differs == "tree" ? "original" : "tree"));
    }
}
