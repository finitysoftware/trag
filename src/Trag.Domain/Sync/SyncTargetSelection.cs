using Trag.Domain.Status;

namespace Trag.Domain.Sync;

// The remote that sync targets and the snapshot it was chosen for. The choice survives a new snapshot of the same
// branch; another branch starts from its tracking remote. Its branch list is worth keeping only while the same remote
// is the target on the same branch, and the remote has to be read again when anything that read depends on changed.
public sealed record SyncTargetSelection(RepositorySnapshot? Snapshot, string? RemoteName)
{
    public static readonly SyncTargetSelection Empty = new(null, null);
    public IReadOnlyList<string> RemoteNames { get; } = Snapshot?.Remotes.Select(remote => remote.Name).ToArray() ?? [];
    public RepositoryRemote? Remote => Snapshot?.Remote(RemoteName);

    public bool IsSameBranch(RepositorySnapshot next) => Snapshot is { } before && before.Root == next.Root && before.Branch == next.Branch;

    // A fetch moved a remote-tracking ref: the same target is read again, keeping what it shows meanwhile.
    public bool RemotesMoved(RepositorySnapshot next) => Snapshot is { } before && !before.HasSameRemotes(next);

    public bool NeedsRead(RepositorySnapshot next) => !IsSameBranch(next)
        || Snapshot!.Head != next.Head || !Snapshot.Remotes.SequenceEqual(next.Remotes)
        || Snapshot.TrackingRemote != next.TrackingRemote || Snapshot.UpstreamBranchRef != next.UpstreamBranchRef;

    // The user's remote stays the target on the same branch while it is there; one renamed or removed gives way to the
    // remote the branch tracks, as a remote renamed in the remotes dialog keeps tracking under its new name.
    public SyncTargetSelection WithSnapshot(RepositorySnapshot next)
    {
        var chosen = IsSameBranch(next) ? next.Remote(RemoteName) : null;
        return new(next, (chosen ?? next.Remote(next.TrackingRemote))?.Name);
    }

    public bool KeepsBranches(SyncTargetSelection next) =>
        next.Snapshot is { } snapshot && IsSameBranch(snapshot) && next.Remote is not null && next.RemoteName == RemoteName;
}
