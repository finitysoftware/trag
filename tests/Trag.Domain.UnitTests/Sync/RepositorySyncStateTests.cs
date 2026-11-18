using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Sync;

public sealed class RepositorySyncStateTests
{
    [Fact]
    public void WhenTheChosenBranchIsListed_ShouldOfferTheListedBranches()
    {
        var (_, _, listed) = Listed();
        Assert.Same(listed.Branches, listed.Choices);
    }

    // The reader lists a bounded number of branches; the chosen one is offered even when it fell outside that list.
    [Fact]
    public void WhenTheBoundedListLeftTheChosenBranchOut_ShouldOfferItAfterTheListedOnes()
    {
        var (trunk, shared, listed) = Listed();
        var bounded = listed with { Branches = [shared] };
        Assert.Equal([shared, trunk], bounded.Choices);
    }

    [Fact]
    public void WhenNoBranchIsChosen_ShouldOfferOnlyTheListedBranches()
    {
        var (_, shared, listed) = Listed();
        var none = listed with { Branch = null, Branches = [shared] };
        Assert.Equal([shared], none.Choices);
    }

    [Fact]
    public void WhenTheTreeIsClean_ShouldSynchronizeTransferAndPull()
    {
        var (state, clean) = (Tracking(), Snapshots.Create(branch: "trunk"));
        Assert.True(state.CanSynchronize); Assert.True(state.CanTransfer(clean)); Assert.True(state.CanPull(clean));
    }

    [Fact]
    public void WhenTheTreeHasChanges_ShouldTransferButNotPull()
    {
        var state = Tracking();
        var dirty = Snapshots.Create(branch: "trunk", files: [Snapshots.Modified("a")]);
        Assert.True(state.CanTransfer(dirty)); Assert.False(state.CanPull(dirty));
    }

    // The writer asks the same rule on the fresh repository and says which part of it keeps the pull back.
    [Fact]
    public void WhenAPullCannotRun_ShouldSayWhetherTheTreeOrARewriteKeepsItBack()
    {
        var state = Tracking();
        var clean = Snapshots.Create(branch: "trunk");
        Assert.Null(state.PullRefusal(clean));
        Assert.Equal(Trag.Domain.Presentation.Text.SyncOutcomeText.PullNeedsCleanTree, state.PullRefusal(Snapshots.Create(branch: "trunk", files: [Snapshots.Modified("a")])));
        Assert.Equal(Trag.Domain.Presentation.Text.SyncOutcomeText.PullAfterRewrite, (state with { Ahead = 1, Behind = 1, Rewritten = true }).PullRefusal(clean));
    }

    [Fact]
    public void WhenTheBranchHasNoCommitYet_ShouldNotTransfer()
    {
        var state = Tracking();
        Assert.False(state.CanTransfer(Snapshots.Create(branch: "trunk", unborn: true)));
    }

    [Fact]
    public void WhenNoRemoteBranchIsChosen_ShouldNotTransfer()
    {
        var (state, clean) = (Tracking(), Snapshots.Create(branch: "trunk"));
        Assert.False((state with { Branch = null }).CanTransfer(clean));
    }

    [Fact]
    public void WhenTheRemoteIsLimited_ShouldNeverSynchronizeNorTransfer()
    {
        var (state, clean) = (Tracking(), Snapshots.Create(branch: "trunk"));
        var limited = state with { Remote = state.Remote with { Limitation = "Unsupported transport." } };
        Assert.False(limited.CanSynchronize); Assert.False(limited.CanTransfer(clean));
    }

    // The upstream has the branch's tip when the branch has nothing it lacks; a branch the remote does not have yet, or
    // counts that could not be read, say nothing about it.
    [Fact]
    public void WhenTheBranchIsNotAheadOfAnExistingUpstream_ShouldSayTheUpstreamHasItsTip()
    {
        var state = Tracking();
        Assert.True(state.UpstreamHasHead); Assert.True((state with { Behind = 3 }).UpstreamHasHead);
        Assert.False((state with { Ahead = 1 }).UpstreamHasHead); Assert.False((state with { Ahead = null }).UpstreamHasHead);
        Assert.False((state with { Branch = state.Branch! with { IsNew = true } }).UpstreamHasHead);
        Assert.False((state with { Branch = null }).UpstreamHasHead);
    }

    // Trunk and shared listed, trunk chosen.
    // Of the commits leaving the branch, newest first, those past the ones the upstream lacks are pushed already.
    [Fact]
    public void WhenCommitsLeaveTheBranch_ShouldCountThoseTheUpstreamAlreadyHas()
    {
        var (_, _, listed) = Listed();
        var ahead = listed with { Ahead = 2 };
        Assert.Equal((0, 0, 3), (ahead.PushedAmong(1), ahead.PushedAmong(2), ahead.PushedAmong(5)));
        Assert.Equal(0, (ahead with { Branch = ahead.Branch! with { IsNew = true } }).PushedAmong(5));
        Assert.Equal(0, (ahead with { Ahead = null }).PushedAmong(5));
    }

    // A branch a push would create on the remote is chosen, but is no upstream yet.
    [Fact]
    public void WhenTheChosenBranchExistsOnTheRemote_ShouldHaveAnUpstream()
    {
        var state = Tracking();
        Assert.True(state.HasUpstream);
        Assert.False((state with { Branch = state.Branch! with { IsNew = true } }).HasUpstream); Assert.False((state with { Branch = null }).HasUpstream);
    }

    private static (SyncBranch Trunk, SyncBranch Shared, RepositorySyncState Listed) Listed()
    {
        var trunk = new SyncBranch("refs/heads/trunk", "refs/remotes/origin/trunk");
        var shared = new SyncBranch("refs/heads/shared", "refs/remotes/origin/shared");
        var remote = new SyncRemote("origin", "fetch", "push", "config");
        return (trunk, shared, new RepositorySyncState(remote, [trunk, shared], trunk, 0, 0, ""));
    }

    // Trunk on origin, chosen.
    private static RepositorySyncState Tracking()
    {
        var trunk = new SyncBranch("refs/heads/trunk", "refs/remotes/origin/trunk");
        return new RepositorySyncState(new SyncRemote("origin", "fetch", "push", "config"), [trunk], trunk, 0, 0, "");
    }
}
