using Trag.Domain.Branches;
using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

public sealed class PublishTargetsTests
{
    [Fact]
    public void WhenTheRemoteHasTheCurrentBranch_ShouldOfferItFirstAndTheOthersOnceEachSorted()
    {
        var index = Index();
        var origin = PublishTargets.For(index, "origin", "main");
        Assert.Equal(["main", "a", "A", "z"], origin.Select(target => target.Name));
        Assert.False(origin[0].IsNew);
    }

    [Fact]
    public void WhenTheRemoteLacksTheCurrentBranch_ShouldOfferItFirstAsNewThenTheRemotesOwn()
    {
        var index = Index();
        var upstream = PublishTargets.For(index, "upstream", "main");
        Assert.Equal(["main", "other"], upstream.Select(target => target.Name));
        Assert.True(upstream[0].IsNew); Assert.Equal("upstream", upstream[1].Remote); Assert.False(upstream[1].IsNew);
    }

    [Fact]
    public void WhenRemoteNamesDifferOnlyInCase_ShouldKeepTheirBranchesApart()
    {
        var index = Index();
        Assert.Equal(["main", "different"], PublishTargets.For(index, "Origin", "main").Select(target => target.Name));
    }

    [Fact]
    public void WhenNoRemoteBranchIsKnown_ShouldOfferOnlyTheCurrentBranch()
    {
        Assert.Equal("main", Assert.Single(PublishTargets.For(PublishTargets.Index([]), "origin", "main")).Name);
    }

    [Fact]
    public void WhenNoRemoteIsChosen_ShouldOfferNothing()
    {
        var index = Index();
        Assert.Empty(PublishTargets.For(index, null, "main"));
    }

    [Fact]
    public void WhenNoBranchIsCheckedOut_ShouldOfferNothing()
    {
        var index = Index();
        Assert.Empty(PublishTargets.For(index, "origin", null));
    }

    // origin fetches refs/heads/* into refs/remotes/origin/feature/*: its branches go by their names on origin, and a ref no
    // mapping writes is no branch origin has.
    [Fact]
    public void WhenTheFetchMappingRenamesTheRemotesBranches_ShouldOfferThemByTheirNamesOnTheRemote()
    {
        var index = PublishTargets.Index([Branch("origin", "feature/main", "main"), Branch("origin", "feature/x", "x"), Branch("origin", "stale", null)]);

        var targets = PublishTargets.For(index, "origin", "main");

        Assert.Equal(["main", "x"], targets.Select(target => target.Name));
        Assert.False(targets[0].IsNew);
    }

    [Fact]
    public void WhenTheTargetIsAnotherExistingBranch_ShouldPushIntoIt()
    {
        Assert.True(new PublishTarget("shared", "origin", false).PushesIntoOther("main"));
    }

    [Fact]
    public void WhenTheTargetHasTheBranchsOwnName_ShouldNotPushIntoAnother()
    {
        Assert.False(new PublishTarget("main", "origin", false).PushesIntoOther("main"));
    }

    [Fact]
    public void WhenTheTargetIsNew_ShouldNotPushIntoAnother()
    {
        Assert.False(new PublishTarget("shared", "origin", true).PushesIntoOther("main"));
    }

    // Origin's branches with a duplicate and a name in two cases, upstream's own branch, a remote named Origin, and a ref
    // that belongs to no remote.
    private static IReadOnlyDictionary<string, string[]> Index() => PublishTargets.Index([
        Branch("origin", "z"), Branch("origin", "main"), Branch("origin", "a"), Branch("origin", "A"), Branch("origin", "a"),
        Branch("upstream", "other"), Branch("Origin", "different"), new("orphan", "refs/remotes/orphan", Snapshots.HeadId, true)]);

    private static BranchInfo Branch(string remote, string name) => Branch(remote, name, name);

    private static BranchInfo Branch(string remote, string name, string? onRemote) =>
        new(remote + "/" + name, "refs/remotes/" + remote + "/" + name, Snapshots.HeadId, true, remote) { NameOnRemote = onRemote };
}
