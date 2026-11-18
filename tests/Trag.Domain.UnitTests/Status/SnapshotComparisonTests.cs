using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// A write and a synchronization do not agree on what may change underneath them: the merge state matters to a write,
// the repository root to a synchronization, and an unknown index fingerprint blocks only the write.
public sealed class SnapshotComparisonTests
{
    private static readonly RepositoryMerge Merge = new(new('b', 40), new('a', 40), "Merge branch 'topic'", "fingerprint", "");

    private static RepositorySnapshot Ready() => Snapshots.Create() with { GitDirectory = "C:/repo/.git" };

    [Fact]
    public void WhenNothingChanged_ShouldSatisfyBothComparisons()
    {
        var snapshot = Snapshots.Create();
        Assert.True(SnapshotComparison.MatchesForWrite(snapshot, snapshot));
        Assert.True(SnapshotComparison.SameStateForSync(snapshot, snapshot));
    }

    [Fact]
    public void WhenTheHeadBranchIndexOrOperationMoved_ShouldFailBothComparisons()
    {
        var snapshot = Snapshots.Create();
        foreach (var changed in new[] { snapshot with { Head = new('c', 40) }, snapshot with { Branch = "other" },
                     snapshot with { IndexFingerprint = "other" }, snapshot with { Operation = RepositoryOperation.Rebase } })
        {
            Assert.False(SnapshotComparison.MatchesForWrite(snapshot, changed));
            Assert.False(SnapshotComparison.SameStateForSync(snapshot, changed));
        }
    }

    // git rm --cached leaves the file on disk: Git lists the index's deletion and the untracked file as two rows of one path.
    [Fact]
    public void WhenAPathIsAStagedDeletionAndAnUntrackedFile_ShouldCompareEachRowWithItsOwn()
    {
        var snapshot = Snapshots.Create() with { Files = [Snapshots.Staged("a.txt", 'D'), Snapshots.Untracked("a.txt")] };

        Assert.True(SnapshotComparison.Unchanged(snapshot, snapshot));
        Assert.False(SnapshotComparison.Unchanged(snapshot, snapshot with { Files = [Snapshots.Staged("a.txt", 'D'), Snapshots.Untracked("b.txt")] }));
    }

    [Fact]
    public void WhenAMergeStarted_ShouldStopAWriteButNotASynchronization()
    {
        var snapshot = Snapshots.Create();
        var merging = snapshot with { Merge = Merge };
        Assert.False(SnapshotComparison.MatchesForWrite(snapshot, merging));
        Assert.True(SnapshotComparison.SameStateForSync(snapshot, merging));
    }

    // Finishing a merge commits the message Git keeps for it, so a message rewritten underneath is a merge other than the
    // one shown.
    [Fact]
    public void WhenOnlyTheMergeMessageChanged_ShouldStopAWrite()
    {
        var merging = Snapshots.Create() with { Merge = Merge };
        Assert.False(SnapshotComparison.MatchesForWrite(merging, merging with { Merge = Merge with { Message = "Merge branch 'other'" } }));
    }

    [Fact]
    public void WhenNothingChangedAndThereIsAGitDirectory_ShouldBeReadyForSync()
    {
        var snapshot = Ready();
        Assert.True(SnapshotComparison.ReadyForSync(snapshot, snapshot));
    }

    // Each clause on its own is enough to refuse, and all three are asked in one place now.
    [Fact]
    public void WhenTheRepositoryIsAnotherOne_ShouldNotBeReadyForSync()
    {
        var snapshot = Ready();
        Assert.False(SnapshotComparison.ReadyForSync(snapshot, snapshot with { Root = "other" }));
    }

    [Fact]
    public void WhenThereIsNoBranch_ShouldNotBeReadyForSync()
    {
        var snapshot = Ready();
        Assert.False(SnapshotComparison.ReadyForSync(snapshot, snapshot with { Branch = null }));
    }

    [Fact]
    public void WhenTheStateIsUnsupported_ShouldNotBeReadyForSync()
    {
        var snapshot = Ready();
        Assert.False(SnapshotComparison.ReadyForSync(snapshot, snapshot with { Limitations = ["A submodule is not supported."] }));
    }

    [Fact]
    public void WhenThereIsNoGitDirectory_ShouldNotBeReadyForSync()
    {
        var snapshot = Ready();
        Assert.False(SnapshotComparison.ReadyForSync(snapshot, snapshot with { GitDirectory = null }));
    }

    [Fact]
    public void WhenTheSnapshotIsOfAnotherRepository_ShouldStopASynchronizationButNotAWrite()
    {
        var snapshot = Snapshots.Create();
        var elsewhere = snapshot with { Root = "other" };
        Assert.True(SnapshotComparison.MatchesForWrite(snapshot, elsewhere));
        Assert.False(SnapshotComparison.SameStateForSync(snapshot, elsewhere));
    }

    [Fact]
    public void WhenTheIndexFingerprintIsUnknown_ShouldNeverMatchForAWriteButStillForASynchronization()
    {
        var unknown = Snapshots.Create(indexFingerprint: "");
        Assert.False(SnapshotComparison.MatchesForWrite(unknown, unknown));
        Assert.True(SnapshotComparison.SameStateForSync(unknown, unknown));
    }
}
