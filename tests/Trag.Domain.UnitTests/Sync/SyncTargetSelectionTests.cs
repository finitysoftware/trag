using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Sync;

public sealed class SyncTargetSelectionTests
{
    [Fact]
    public void WhenTheFirstSnapshotComes_ShouldTargetTheTrackingRemoteAndListAllRemotes()
    {
        var first = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        Assert.Equal("origin", first.RemoteName); Assert.Equal(["origin", "backup"], first.RemoteNames);
    }

    [Fact]
    public void WhenASnapshotKeepsTheBranch_ShouldKeepTheUsersRemoteAndItsBranches()
    {
        var (_, chosen, next) = ChosenAndNext();
        Assert.Equal("backup", next.Remote!.Name);
        Assert.True(chosen.KeepsBranches(next));
    }

    [Fact]
    public void WhenTheRemoteChanged_ShouldNotKeepTheBranches()
    {
        var (first, _, next) = ChosenAndNext();
        Assert.False(first.KeepsBranches(next));
    }

    [Fact]
    public void WhenAnotherBranchWithoutUpstreamIsCheckedOut_ShouldDropTheRemoteAndItsBranches()
    {
        var current = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var changed = current.WithSnapshot(Snapshot() with { Branch = "topic", TrackingRemote = null, UpstreamBranchRef = null });
        Assert.Null(changed.Remote); Assert.False(current.KeepsBranches(changed));
    }

    [Fact]
    public void WhenTheTargetRemoteIsRemoved_ShouldDropItAndItsBranches()
    {
        var current = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var removed = current.WithSnapshot(Snapshot() with { Remotes = [new("backup", "backup-fetch", "backup-push")] });
        Assert.Null(removed.Remote); Assert.False(current.KeepsBranches(removed));
    }

    // The tracked remote is renamed, as the remotes dialog does with git remote rename: the target follows the branch to
    // the new name instead of losing its remote.
    [Fact]
    public void WhenTheTargetRemoteIsRenamed_ShouldFollowTheBranchToItsNewName()
    {
        var current = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var renamed = current.WithSnapshot(Snapshot() with
        {
            Remotes = [new("upstream", "origin-fetch", "origin-push"), new("backup", "backup-fetch", "backup-push")], TrackingRemote = "upstream"
        });
        Assert.Equal("upstream", renamed.Remote?.Name);
    }

    [Fact]
    public void WhenOnlyFilesChanged_ShouldNotNeedARead()
    {
        var snapshot = Snapshot();
        var selection = SyncTargetSelection.Empty.WithSnapshot(snapshot);
        Assert.False(selection.NeedsRead(snapshot with { Files = [Snapshots.Modified("a")] }));
    }

    [Fact]
    public void WhenNothingWasReadYet_ShouldNeedARead()
    {
        var snapshot = Snapshot();
        Assert.True(SyncTargetSelection.Empty.NeedsRead(snapshot));
    }

    [Fact]
    public void WhenTheBranchHeadRemotesOrUpstreamChanged_ShouldNeedARead()
    {
        var snapshot = Snapshot();
        var selection = SyncTargetSelection.Empty.WithSnapshot(snapshot);
        foreach (var changed in new[]
        {
            snapshot with { Branch = "topic" },
            snapshot with { Head = new string('d', 40) },
            snapshot with { Remotes = [new("origin", "fetch", "new-push")] },
            snapshot with { TrackingRemote = "backup" },
            snapshot with { UpstreamBranchRef = "refs/heads/shared" }
        }) Assert.True(selection.NeedsRead(changed));
    }

    [Fact]
    public void WhenARemoteTrackingRefMoved_ShouldReadTheSameTargetAgainWithoutStartingOver()
    {
        var (snapshot, selection) = ReadAtFirstRefs();
        var fetched = snapshot with { RemoteRefs = "r2" };
        Assert.True(selection.RemotesMoved(fetched)); Assert.False(selection.NeedsRead(fetched));
    }

    [Fact]
    public void WhenOnlyFilesChanged_ShouldNotCountAsMovedRemotes()
    {
        var (snapshot, selection) = ReadAtFirstRefs();
        Assert.False(selection.RemotesMoved(snapshot with { Files = [Snapshots.Modified("a")] }));
    }

    [Fact]
    public void WhenNothingWasReadYet_ShouldNotCountAsMovedRemotes()
    {
        var (snapshot, _) = ReadAtFirstRefs();
        var fetched = snapshot with { RemoteRefs = "r2" };
        Assert.False(SyncTargetSelection.Empty.RemotesMoved(fetched));
    }

    private static RepositorySnapshot Snapshot() => Snapshots.Create(branch: "trunk", trackingRemote: "origin",
        upstreamBranchRef: "refs/heads/trunk", remotes: [new("origin", "fetch", "push"), new("backup", "backup-fetch", "backup-push")]);

    // The first target, the user's choice of backup over it, and what that choice became for a snapshot that changed
    // only files.
    private static (SyncTargetSelection First, SyncTargetSelection Chosen, SyncTargetSelection Next) ChosenAndNext()
    {
        var first = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var chosen = first with { RemoteName = "backup" };
        return (first, chosen, chosen.WithSnapshot(Snapshot() with { Files = [Snapshots.Modified("changed.txt")] }));
    }

    // A target read when the remote-tracking refs were at r1.
    private static (RepositorySnapshot Snapshot, SyncTargetSelection Selection) ReadAtFirstRefs()
    {
        var snapshot = Snapshot() with { RemoteRefs = "r1" };
        return (snapshot, SyncTargetSelection.Empty.WithSnapshot(snapshot));
    }
}
