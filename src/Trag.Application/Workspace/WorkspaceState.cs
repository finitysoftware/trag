using System.Diagnostics.CodeAnalysis;
using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// The one lock of a workspace: which slot is current, whether the workspace is disposed, and every decision that must
// see the current slot, its write and its snapshot together. Services own the flows; this owns the transitions.
public sealed class WorkspaceState
{
    private readonly object sync = new();
    private RepositorySlot? current;
    private bool disposed;

    public RepositorySlot? Current { get { lock (sync) return current; } }

    // Stop accepting work before waiting, so the write that holds the session is the last one.
    public Task BeginDispose()
    {
        lock (sync)
        {
            disposed = true;
            return current?.Write?.Task ?? Task.CompletedTask;
        }
    }

    public RepositorySlot Replace(string path, out RepositorySlot? previous)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current?.Write is not null)
                throw new InvalidOperationException("Wait for the Git operation before switching repositories.");
            previous = current;
            return current = new(path);
        }
    }

    public RepositorySlot? Detach()
    {
        lock (sync)
        {
            if (current?.Write is not null)
                throw new InvalidOperationException("Wait for the Git operation before closing the repository.");
            var previous = current;
            current = null;
            return previous;
        }
    }

    public bool Holds(RepositorySnapshot snapshot)
    {
        lock (sync)
            return ReferenceEquals(current?.Snapshots.Snapshot, snapshot);
    }

    // A watch stopped by a refresh begins no read after the refresh's: the stop comes first, and both begin under the lock.
    public bool TryBeginRefresh(RepositorySlot slot, out long version, CancellationToken ct = default)
    {
        lock (sync)
        {
            version = 0;
            if (ct.IsCancellationRequested || !CanRead(slot))
                return false;
            version = slot.Lease.Invalidate();
            return true;
        }
    }

    // A read result becomes the shown snapshot only for the current slot, with no write in flight and no newer read.
    // The change source is kept by the first accepted read and discarded by every later one.
    public RepositorySnapshot? Accept(RepositorySlot slot, long version, RepositorySnapshot snapshot, IRepositoryChangeSource? source)
    {
        lock (sync)
        {
            if (!CanRead(slot) || !slot.Lease.IsCurrent(version))
            {
                source?.Dispose();
                return null;
            }
            if (slot.Changes is null)
                slot.Changes = source;
            else
                source?.Dispose();
            return slot.Snapshots.Set(snapshot);
        }
    }

    // History-style reads: the caller's snapshot must still name the same root, head and branch, with no write in flight.
    public bool TryBeginHistoryRead(RepositorySnapshot expected, CancellationToken ct,
        [NotNullWhen(true)] out RepositorySlot? slot, [NotNullWhen(true)] out CancellationTokenSource? linked)
    {
        lock (sync)
        {
            slot = null;
            linked = null;
            if (!CanRead(current) || !expected.HasSameHistory(current.Snapshots.Snapshot))
                return false;
            slot = current;
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, slot.Lifetime, slot.Snapshots.HistoryToken);
            return true;
        }
    }

    public bool StillMatches(RepositorySlot slot, RepositorySnapshot expected, CancellationToken ct)
    {
        lock (sync)
        {
            // A completed write can invalidate this token without changing HEAD.
            ct.ThrowIfCancellationRequested();
            return CanRead(slot) && expected.HasSameHistory(slot.Snapshots.Snapshot);
        }
    }

    // Working-tree reads: the snapshot shown at the start must still be shown when the read ends, with no write in flight
    // and none ended since; every ended write cancels the history token, even one whose final status read failed.
    // A refresh that has only begun leaves the read standing: it begins as soon as an update is shown while changes keep
    // coming, so dropping the read for it could drop every read.
    public WorkingDiffRead? BeginWorkingRead()
    {
        lock (sync)
        {
            if (!CanRead(current) || current.Snapshots.Snapshot is not { } shown)
                return null;
            return new WorkingDiffRead(this, current, shown, current.Snapshots.HistoryToken);
        }
    }

    public bool StillShows(RepositorySlot slot, RepositorySnapshot shown, CancellationToken written)
    {
        lock (sync)
            return CanRead(slot) && ReferenceEquals(slot.Snapshots.Snapshot, shown) && !written.IsCancellationRequested;
    }

    public bool StillCurrent(RepositorySlot slot, long version)
    {
        lock (sync)
            return CanRead(slot) && slot.Lease.IsCurrent(version);
    }

    // Writes: the slot must show a snapshot of the requested root and hold no other write; this one holds the session until
    // it is verified (EndWrite).
    public bool TryBeginWrite(string root, [NotNullWhen(true)] out RepositorySlot? slot)
    {
        lock (sync)
        {
            slot = current;
            if (disposed || slot?.Snapshots.Snapshot?.Root != root || slot.Write is not null)
                return false;
            slot.Write = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    public void SetIfCurrent(RepositorySlot slot, RepositorySnapshot snapshot)
    {
        lock (sync)
        {
            if (current == slot)
                slot.Snapshots.Set(snapshot);
        }
    }

    // Lets the session go and hands back the change source to reconcile, or null when the slot is no longer current.
    public IRepositoryChangeSource? EndWrite(RepositorySlot slot)
    {
        lock (sync)
        {
            slot.Snapshots.InvalidateHistoryReads();
            slot.Write?.TrySetResult();
            slot.Write = null;
            return current == slot ? slot.Changes : null;
        }
    }

    // Called under sync; used both before a read and when accepting its result.
    private bool CanRead([NotNullWhen(true)] RepositorySlot? slot) =>
        !disposed && slot is not null && current == slot && slot.Write is null;
}
