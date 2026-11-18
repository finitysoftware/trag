using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// The snapshot a session currently shows and the lifetime of history reads made against it. It changes only under the
// workspace lock: worktree-only updates keep history reads valid, a new root, head or branch cancels them. The live change
// stream reads Snapshot without the lock, which is safe because the property holds one immutable record at a time.
public sealed class SnapshotStore
{
    private CancellationTokenSource historyLifetime = new();

    public RepositorySnapshot? Snapshot { get; private set; }
    public CancellationToken HistoryToken => historyLifetime.Token;

    public RepositorySnapshot Set(RepositorySnapshot snapshot)
    {
        if (Snapshot is { } previous && !previous.HasSameHistory(snapshot))
            InvalidateHistoryReads();
        return Snapshot = snapshot;
    }

    // A write can change refs or stashes without moving HEAD. Pending reads must still be discarded.
    public void InvalidateHistoryReads()
    {
        var previous = historyLifetime;
        historyLifetime = new();
        previous.Cancel();
        previous.Dispose();
    }
}
