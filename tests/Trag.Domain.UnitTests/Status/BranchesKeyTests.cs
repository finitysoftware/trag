using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// What the branch list follows: two snapshots list the same branches when the checked-out branch, every local and
// remote-tracking ref and the remotes' push addresses are the same, whatever else moved between them.
public sealed class BranchesKeyTests
{
    [Fact]
    public void WhenOnlyTheFilesTheIndexAnOperationOrTheStashMoved_ShouldKeepTheKey()
    {
        var snapshot = Snapshots.Create();
        foreach (var same in new[] { snapshot with { Files = [Snapshots.Modified("a.txt")] }, snapshot with { IndexFingerprint = "other" },
                     snapshot with { Operation = RepositoryOperation.CherryPick }, snapshot with { StashHead = Snapshots.Id('s') } })
            Assert.Equal(snapshot.BranchesKey, same.BranchesKey);
    }

    [Fact]
    public void WhenTheBranchALocalRefARemoteTrackingRefOrARemotesAddressMoved_ShouldChangeTheKey()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "https://github.com/finity/trag.git")]);
        foreach (var moved in new[] { snapshot with { Branch = "topic" }, snapshot with { Branch = null }, snapshot with { LocalRefs = "committed" }, snapshot with { RemoteRefs = "fetched" },
                     snapshot with { Remotes = [Snapshots.Remote("origin", "https://gitlab.com/finity/trag.git")] } })
            Assert.NotEqual(snapshot.BranchesKey, moved.BranchesKey);
    }
}
