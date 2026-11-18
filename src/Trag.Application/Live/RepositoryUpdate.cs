using Trag.Domain.Status;

namespace Trag.Application.Live;

public sealed record RepositoryUpdate(RepositorySnapshot? Snapshot, bool IsPolling = false)
{
    // What the snapshot is known to have changed; everything unless the watcher named the paths.
    public SnapshotChange Change { get; init; } = SnapshotChange.Everything;
}
