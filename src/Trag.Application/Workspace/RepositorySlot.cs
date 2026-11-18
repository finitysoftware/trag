using Trag.Application.Operations;
using Trag.Application.Ports.Watching;

namespace Trag.Application.Workspace;

// One opened repository: its lease (identity and version), operation coordinator, lifetime, shown snapshot, the write that
// holds it and change source.
public sealed class RepositorySlot(string path)
{
    public RepositoryLease Lease { get; } = new(path);
    public OperationCoordinator Operations { get; } = new();
    public CancellationToken Lifetime => Operations.Lifetime;
    public SnapshotStore Snapshots { get; } = new();
    // The one write holding the session, from its start until it is verified; set and cleared under the workspace lock.
    public TaskCompletionSource? Write { get; set; }
    // Assigned once under the workspace lock; null until the first accepted snapshot attaches a watcher.
    public IRepositoryChangeSource? Changes { get; set; }

    public async Task RetireAsync()
    {
        Lease.Invalidate();
        await Operations.DisposeAsync().ConfigureAwait(false);
        Changes?.Dispose();
    }
}
