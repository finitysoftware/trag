using Trag.ViewModels.Sync;

namespace Trag.ViewModels.UnitTests.Sync;

// What the upstream selector writes for a branch, a branch the remote does not have yet included: the row shows Domain's
// SyncText.BranchLabel, and these pin the cases the row meets (SyncTextTests has the words for another branch).
public sealed class SyncBranchRowTests
{
    private static SyncBranchRow Row(bool isNew) => new(new("refs/heads/main", "refs/remotes/origin/main") { IsNew = isNew });

    [Fact]
    public void WhenTheRemoteDoesNotHaveTheBranchYet_ShouldSayItIsANewBranch() => Assert.Equal("main · new branch", Row(true).Label);

    [Fact]
    public void WhenTheRemoteAlreadyHasTheBranch_ShouldShowJustItsName() => Assert.Equal("main", Row(false).Label);

    [Fact]
    public void WhenTheRefIsNotABranch_ShouldShowItsWholeName() =>
        Assert.Equal("refs/tags/v1", new SyncBranchRow(new("refs/tags/v1", "refs/remotes/origin/v1")).Label);
}
