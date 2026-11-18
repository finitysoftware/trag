using Trag.Domain.Branches;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Branches;

public sealed class BranchMovesTests
{
    private static readonly BranchInfo Main = new("main", "refs/heads/main", "1", false);
    private static readonly BranchInfo Topic = new("topic", "refs/heads/topic", "2", false);
    private static readonly BranchInfo OriginMain = new("origin/main", "refs/remotes/origin/main", "1", true, "origin");
    private static readonly BranchInfo OriginTopic = new("origin/topic", "refs/remotes/origin/topic", "2", true, "origin");
    private static readonly BranchInfo OriginNew = new("origin/new", "refs/remotes/origin/new", "3", true, "origin");
    private static readonly BranchCatalog Catalog = BranchCatalog.Create(new([Main, Topic], [OriginMain, OriginTopic, OriginNew], "main"), Snapshots.Create());

    [Fact]
    public void WhenALocalBranchIsChosen_ShouldSwitchToIt()
    {
        Assert.Equal(new BranchMove(BranchAction.Switch, new("topic")), BranchMoves.SwitchTo(Topic, Catalog));
    }

    [Fact]
    public void WhenARemoteBranchHasALocalCounterpart_ShouldSwitchToTheCounterpart()
    {
        Assert.Equal(new BranchMove(BranchAction.Switch, new("topic")), BranchMoves.SwitchTo(OriginTopic, Catalog));
    }

    [Fact]
    public void WhenARemoteBranchHasNoLocalCounterpart_ShouldCreateOneThatTracksIt()
    {
        Assert.Equal(new BranchMove(BranchAction.Create, new("new", null, true, "refs/remotes/origin/new")), BranchMoves.SwitchTo(OriginNew, Catalog));
    }

    [Fact]
    public void WhenARemoteBranchsCounterpartIsTheCurrentBranch_ShouldMoveNowhere()
    {
        Assert.Null(BranchMoves.SwitchTo(OriginMain, Catalog));
    }

    // Git keeps a branch as a file whose name ignores case on Windows, so a remote branch whose name differs from a local
    // one only by case has that branch as its counterpart: a second one would hide it.
    [Fact]
    public void WhenARemoteBranchMatchesALocalOneOnlyByCase_ShouldTakeTheLocalOneAsItsCounterpart()
    {
        var originTopic = new BranchInfo("origin/Topic", "refs/remotes/origin/Topic", "4", true, "origin");
        var originMain = new BranchInfo("origin/MAIN", "refs/remotes/origin/MAIN", "5", true, "origin");
        var catalog = BranchCatalog.Create(new([Main, Topic], [originTopic, originMain], "main"), Snapshots.Create());

        Assert.Equal(new BranchMove(BranchAction.Switch, new("topic")), BranchMoves.SwitchTo(originTopic, catalog));
        Assert.Null(BranchMoves.SwitchTo(originMain, catalog));
    }

    [Fact]
    public void WhenALocalBranchIsDeleted_ShouldLetGitRefuseItAsUnmerged()
    {
        var expected = Snapshots.Create();
        Assert.Equal(new BranchRequest(expected, new("topic"), BranchAction.Delete), BranchMoves.Delete(expected, Topic, confirmed: false));
    }

    // Git refused it as unmerged before the user confirmed.
    [Fact]
    public void WhenTheUserConfirmsDeletingALocalBranch_ShouldForceIt()
    {
        var expected = Snapshots.Create();
        Assert.Equal(new BranchRequest(expected, new("topic", ForceAt: "2"), BranchAction.Delete), BranchMoves.Delete(expected, Topic, confirmed: true));
    }

    [Fact]
    public void WhenTheUserConfirmsDeletingARemoteBranch_ShouldDeleteItsNameOnItsRemote()
    {
        var expected = Snapshots.Create();
        Assert.Equal(new RemoteBranchDeleteRequest(expected, "origin", "topic"), BranchMoves.Delete(expected, OriginTopic, confirmed: true));
    }
}
