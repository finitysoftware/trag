using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class SyncTextTests
{
    // A search keeps the branches whose label holds the trimmed text in any case; a blank one keeps every branch.
    [Fact]
    public void WhenUpstreamsAreSearched_ShouldMatchTheLabelByTheTrimmedTextInAnyCase()
    {
        var shared = new SyncBranch("refs/heads/shared", "refs/remotes/origin/shared");
        Assert.True(SyncText.Matches(shared, " SH "));
        Assert.True(SyncText.Matches(shared, " "));
        Assert.False(SyncText.Matches(shared, "trunk"));
    }

    [Fact]
    public void WhenTheBranchIsOnTheRemote_ShouldBeLabeledByItsName()
    {
        var trunk = Trunk();
        Assert.Equal("trunk", SyncText.BranchLabel(trunk));
    }

    [Fact]
    public void WhenTheBranchIsNew_ShouldSayNewBranch()
    {
        var trunk = Trunk();
        Assert.Equal("trunk · new branch", SyncText.BranchLabel(trunk with { IsNew = true }));
    }

    [Fact]
    public void WhenTheBranchIsNotOnTheRemoteYet_ShouldSaySoInsteadOfCounting()
    {
        var state = NewTrunk();
        Assert.Equal("trunk is not on origin yet", SyncCounts.From(state, null, DateTimeOffset.UnixEpoch).Text);
    }

    [Fact]
    public void WhenTheBranchIsOnTheRemote_ShouldCountHowFarAheadAndBehindItIs()
    {
        var (trunk, state) = (Trunk(), NewTrunk());
        Assert.Equal("↑ 1 ahead  ·  ↓ 0 behind", SyncCounts.From(state with { Branch = trunk, Ahead = 1, Behind = 0 }, null, DateTimeOffset.UnixEpoch).Text);
    }

    [Fact]
    public void WhenNoRemoteIsChosen_ShouldAskToChooseOne() => Assert.Equal("Choose a remote", SyncTarget.Empty(null).FetchAddress);

    private static SyncBranch Trunk() => new("refs/heads/trunk", "refs/remotes/origin/trunk");

    // trunk, not on origin yet, with nothing counted.
    private static RepositorySyncState NewTrunk()
    {
        var trunk = Trunk();
        return new(new("origin", "fetch", "push", "config"), [trunk], trunk with { IsNew = true }, null, null, "");
    }
}
