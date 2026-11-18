using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Status;

public sealed class RepositorySnapshotTests
{
    private static readonly RepositoryMerge Merge = new("incoming", Snapshots.HeadId, "Merge message", "fingerprint", "");
    private static RepositorySnapshot Merging() => Snapshots.Create(operation: RepositoryOperation.Merge, merge: Merge);

    [Fact]
    public void WhenAMergeHasABranchAHeadNoLimitationsAndConsistentMetadata_ShouldBeSupported()
    {
        Assert.True(Merging().HasSupportedMerge);
    }

    [Fact]
    public void WhenAMergeRunsOnADetachedHead_ShouldNotBeSupported()
    {
        Assert.False((Merging() with { Branch = null }).HasSupportedMerge);
    }

    [Fact]
    public void WhenAMergeHasNoHead_ShouldNotBeSupported()
    {
        Assert.False((Merging() with { Head = null }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheRepositoryHasALimitation_ShouldNotSupportTheMerge()
    {
        Assert.False((Merging() with { Limitations = ["Sparse checkout"] }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheOperationIsARebase_ShouldNotSupportAMerge()
    {
        Assert.False((Merging() with { Operation = RepositoryOperation.Rebase }).HasSupportedMerge);
    }

    [Fact]
    public void WhenGitReportsNoOperation_ShouldNotSupportAMerge()
    {
        Assert.False((Merging() with { Operation = RepositoryOperation.None }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheMergeHasALimitationOfItsOwn_ShouldNotBeSupported()
    {
        Assert.False((Merging() with { Merge = Merge with { Limitation = "autostash" } }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheMergeStartedFromAnotherHead_ShouldNotBeSupported()
    {
        Assert.False((Merging() with { Merge = Merge with { OriginalHead = Snapshots.Id('b') } }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheIncomingCommitIsUnknown_ShouldNotSupportTheMerge()
    {
        Assert.False((Merging() with { Merge = Merge with { IncomingHead = null } }).HasSupportedMerge);
    }

    [Fact]
    public void WhenTheMergesMetadataIsMissing_ShouldNotSupportTheMerge()
    {
        Assert.False((Merging() with { Merge = null }).HasSupportedMerge);
    }

    [Fact]
    public void WhenAMergeIsInProgress_ShouldHaveAMergeIdentity()
    {
        var merging = Merging();
        Assert.NotNull(merging.MergeIdentity);
    }

    [Fact]
    public void WhenOnlyTheFilesOfAMergeChange_ShouldKeepItsIdentity()
    {
        var merging = Merging();
        Assert.Equal(merging.MergeIdentity, (merging with { Files = [Snapshots.Modified("a")] }).MergeIdentity);
    }

    [Fact]
    public void WhenTheIncomingCommitChanges_ShouldChangeTheMergeIdentity()
    {
        var merging = Merging();
        Assert.NotEqual(merging.MergeIdentity, (merging with { Merge = Merge with { IncomingHead = Snapshots.Id('c') } }).MergeIdentity);
    }

    [Fact]
    public void WhenTheMergeIsInAnotherRepository_ShouldChangeTheMergeIdentity()
    {
        var merging = Merging();
        Assert.NotEqual(merging.MergeIdentity, (merging with { Root = "elsewhere" }).MergeIdentity);
    }

    [Fact]
    public void WhenNoMergeIsInProgress_ShouldHaveNoMergeIdentity()
    {
        var merging = Merging();
        Assert.Null((merging with { Merge = null }).MergeIdentity);
    }

    [Fact]
    public void WhenTheRepositoryIsOnABranchWithNothingInProgress_ShouldFlagNothing()
    {
        var clean = Snapshots.Create();
        Assert.False(clean.HasUnsupportedState); Assert.False(clean.HasUnsupportedWriteState); Assert.False(clean.IsDetached); Assert.False(clean.IsUnborn);
    }

    [Fact]
    public void WhenTheRepositoryHasALimitation_ShouldBeUnsupportedForWritesToo()
    {
        var clean = Snapshots.Create();
        var limited = clean with { Limitations = ["Linked worktree"] };
        Assert.True(limited.HasUnsupportedState); Assert.True(limited.HasUnsupportedWriteState);
    }

    [Fact]
    public void WhenAnotherOperationIsInProgress_ShouldBeUnsupportedForWritesToo()
    {
        var clean = Snapshots.Create();
        var busy = clean with { Operation = RepositoryOperation.CherryPick };
        Assert.True(busy.HasUnsupportedState); Assert.True(busy.HasUnsupportedWriteState);
    }

    // Sync and publish need a branch; staging, committing and moving between commits also work on a detached HEAD.
    [Fact]
    public void WhenHeadIsDetached_ShouldBeUnsupportedButStillTakeWritesAndCountItsOwnCommits()
    {
        var detached = Snapshots.Create(branch: null, detachedCommits: 3);
        Assert.True(detached.IsDetached); Assert.True(detached.HasUnsupportedState); Assert.False(detached.HasUnsupportedWriteState); Assert.Equal(3, detached.DetachedCommits);
    }

    [Fact]
    public void WhenTheBranchHasNoCommitYet_ShouldBeUnbornButNeitherDetachedNorUnsupported()
    {
        var unborn = Snapshots.Create(unborn: true);
        Assert.True(unborn.IsUnborn); Assert.False(unborn.IsDetached); Assert.False(unborn.HasUnsupportedState);
    }

    [Fact]
    public void WhenOnlyTheWorkingTreeChanges_ShouldKeepTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        Assert.True(snapshot.HasSameHistory(snapshot with { Files = [Snapshots.Modified("a")] }));
    }

    [Fact]
    public void WhenThereIsNoOtherSnapshot_ShouldNotHaveTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.HasSameHistory(null));
    }

    [Fact]
    public void WhenTheRepositoryIsAnother_ShouldNotHaveTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.HasSameHistory(snapshot with { Root = "elsewhere" }));
    }

    [Fact]
    public void WhenHeadMoved_ShouldNotHaveTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.HasSameHistory(snapshot with { Head = Snapshots.Id('b') }));
    }

    [Fact]
    public void WhenTheBranchIsAnother_ShouldNotHaveTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.HasSameHistory(snapshot with { Branch = "other" }));
    }

    [Fact]
    public void WhenAnUnbornBranchOnlyGainsFiles_ShouldKeepTheSameHistory()
    {
        var unborn = Snapshots.Create(unborn: true);
        Assert.True(unborn.HasSameHistory(unborn with { Files = [Snapshots.Untracked("new")] }));
    }

    [Fact]
    public void WhenADetachedHeadOnlyGainsChanges_ShouldKeepTheSameHistory()
    {
        var detached = Snapshots.Create(branch: null);
        Assert.True(detached.HasSameHistory(detached with { Files = [Snapshots.Modified("a")] }));
    }

    [Fact]
    public void WhenHeadIsDetachedAtTheCommitTheBranchWasOn_ShouldNotHaveTheSameHistory()
    {
        var snapshot = Snapshots.Create();
        var detached = Snapshots.Create(branch: null);
        Assert.False(snapshot.HasSameHistory(detached));
    }

    // A pending choice belongs to one repository and one head.
    [Fact]
    public void WhenTheRepositoryAndHeadAreTheSame_ShouldBeTheSameHead()
    {
        var snapshot = Snapshots.Create();
        Assert.True(snapshot.IsSameHead(snapshot.Root, snapshot.Head));
    }

    [Fact]
    public void WhenTheRepositoryIsAnother_ShouldNotBeTheSameHead()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.IsSameHead("elsewhere", snapshot.Head));
    }

    [Fact]
    public void WhenHeadMoved_ShouldNotBeTheSameHead()
    {
        var snapshot = Snapshots.Create();
        Assert.False(snapshot.IsSameHead(snapshot.Root, Snapshots.Id('b')));
    }

    [Fact]
    public void WhenThereIsNoRemote_ShouldPreferNoneAndNeedPublish()
    {
        var none = Snapshots.Create(remotes: [], trackingRemote: null);
        Assert.Null(none.PreferredRemote); Assert.True(none.NeedsPublish);
    }

    [Fact]
    public void WhenTheBranchTracksOriginAmongOtherRemotes_ShouldPreferOriginAndNotNeedPublish()
    {
        var (origin, _, two) = TwoRemotesTrackingOrigin();
        Assert.Same(origin, two.PreferredRemote); Assert.False(two.NeedsPublish);
    }

    [Fact]
    public void WhenThereIsNoOrigin_ShouldPreferTheFirstRemote()
    {
        var (_, backup, two) = TwoRemotesTrackingOrigin();
        Assert.Same(backup, (two with { Remotes = [backup] }).PreferredRemote);
    }

    [Fact]
    public void WhenTheBranchTracksNoRemote_ShouldNeedPublishInsteadOfPush()
    {
        var (_, _, two) = TwoRemotesTrackingOrigin();
        Assert.True((two with { TrackingRemote = null }).NeedsPublish);
    }

    [Fact]
    public void WhenHeadIsDetached_ShouldNotNeedPublish()
    {
        var (_, _, two) = TwoRemotesTrackingOrigin();
        Assert.False((two with { Branch = null, TrackingRemote = null }).NeedsPublish);
    }

    [Fact]
    public void WhenNoRemoteWasChosen_ShouldKeepNone()
    {
        var (_, _, two) = TwoRemotesTrackingOrigin();
        Assert.Null(two.KeepRemote(null));
    }

    [Fact]
    public void WhenTheChosenRemoteStillExists_ShouldKeepItAsTheSnapshotKnowsIt()
    {
        var (_, backup, two) = TwoRemotesTrackingOrigin();
        Assert.Same(backup, two.KeepRemote(new("backup", "old", "old")));
    }

    [Fact]
    public void WhenTheChosenRemoteIsGone_ShouldKeepTheFirstRemote()
    {
        var (_, backup, two) = TwoRemotesTrackingOrigin();
        Assert.Same(backup, two.KeepRemote(new("gone", "g", "g")));
    }

    [Fact]
    public void WhenOnlyTheIndexChanged_ShouldBeStagedOnly()
    {
        Assert.True(Snapshots.Staged("a").IsStaged); Assert.False(Snapshots.Staged("a").IsUnstaged);
    }

    [Fact]
    public void WhenOnlyTheWorkingTreeChanged_ShouldBeUnstagedOnly()
    {
        Assert.False(Snapshots.Modified("a").IsStaged); Assert.True(Snapshots.Modified("a").IsUnstaged);
    }

    [Fact]
    public void WhenBothTheIndexAndTheWorkingTreeChanged_ShouldBeStagedAndUnstaged()
    {
        Assert.True(Snapshots.PartiallyStaged("a").IsStaged); Assert.True(Snapshots.PartiallyStaged("a").IsUnstaged);
    }

    [Fact]
    public void WhenAFileIsUntracked_ShouldBeUnstagedOnly()
    {
        Assert.False(Snapshots.Untracked("a").IsStaged); Assert.True(Snapshots.Untracked("a").IsUnstaged);
    }

    [Fact]
    public void WhenAFileIsInConflict_ShouldBeUnstagedOnly()
    {
        var conflict = Snapshots.Conflict("a");
        Assert.False(conflict.IsStaged); Assert.True(conflict.IsUnstaged);
    }

    [Fact]
    public void WhenAFileIsIgnored_ShouldBeNeitherStagedNorUnstaged()
    {
        var ignored = new FileStatus(Snapshots.Path("a"), '!', '!');
        Assert.False(ignored.IsStaged); Assert.False(ignored.IsUnstaged);
    }

    [Fact]
    public void WhenHeadIsOnABranch_ShouldNotBeDetachedAtACommit()
    {
        var onBranch = Snapshots.Create();
        Assert.False(onBranch.IsDetachedAtCommit); Assert.False(onBranch.LeavesCommitsBehind);
    }

    [Fact]
    public void WhenADetachedHeadHasCommitsOfItsOwn_ShouldLeaveThemBehindOnAMove()
    {
        var detached = Snapshots.Create(branch: null, detachedCommits: 2);
        Assert.True(detached.IsDetachedAtCommit); Assert.True(detached.LeavesCommitsBehind);
    }

    [Fact]
    public void WhenADetachedHeadHasNoCommitsOfItsOwn_ShouldLeaveNothingBehind()
    {
        Assert.False(Snapshots.Create(branch: null).LeavesCommitsBehind);
    }

    [Fact]
    public void WhenADetachedHeadHasNoCommitAtAll_ShouldNotBeDetachedAtACommit()
    {
        Assert.False(Snapshots.Create(branch: null, unborn: true).IsDetachedAtCommit);
    }

    [Fact]
    public void WhenAuthorAndCommitterAreKnown_ShouldHaveACommitIdentity()
    {
        var onBranch = Snapshots.Create();
        Assert.True(onBranch.HasCommitIdentity);
    }

    [Fact]
    public void WhenNoIdentityIsConfigured_ShouldHaveNoCommitIdentity()
    {
        Assert.False(Snapshots.Create(identity: false).HasCommitIdentity);
    }

    [Fact]
    public void WhenTheCommitterIsUnknown_ShouldHaveNoCommitIdentity()
    {
        var onBranch = Snapshots.Create();
        Assert.False((onBranch with { Committer = null }).HasCommitIdentity);
    }

    [Fact]
    public void WhenNoFileChanged_ShouldHaveNoChanges()
    {
        var onBranch = Snapshots.Create();
        Assert.False(onBranch.HasChanges);
    }

    // A submodule's own working tree, moved to another commit or with files of its own, is left alone by Git's merges,
    // rebases and picks; a submodule commit the index holds is a change like any other.
    [Fact]
    public void WhenOnlyASubmodulesOwnWorkingTreeChanged_ShouldHaveNoChanges()
    {
        var own = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, IsSubmoduleMoved: true, SubmoduleHasOwnChanges: true)]);
        var staged = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), 'M', '.', IsSubmodule: true)]);

        Assert.Equal((false, true), (own.HasChanges, staged.HasChanges));
    }

    [Fact]
    public void WhenFilesChangedInAnyWay_ShouldHaveChangesAndListTheConflictsApart()
    {
        var changed = Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Conflict("c"), Snapshots.Untracked("u")]);
        Assert.True(changed.HasChanges);
        Assert.Equal("c", Assert.Single(changed.Conflicts).Path.Display);
    }

    [Fact]
    public void WhenARemoteIsLookedUpByName_ShouldFindThatRemoteOrNone()
    {
        var (origin, backup, two) = TwoRemotesTrackingOrigin();
        Assert.Same(backup, two.Remote("backup")); Assert.Same(origin, two.Remote("origin")); Assert.Null(two.Remote("upstream"));
    }

    // backup listed before origin, and the branch tracking origin.
    private static (RepositoryRemote Origin, RepositoryRemote Backup, RepositorySnapshot Two) TwoRemotesTrackingOrigin()
    {
        RepositoryRemote origin = new("origin", "o", "o"), backup = new("backup", "b", "b");
        return (origin, backup, Snapshots.Create(remotes: [backup, origin], trackingRemote: "origin"));
    }
}
