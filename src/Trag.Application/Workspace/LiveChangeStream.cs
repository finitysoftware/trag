using System.Runtime.CompilerServices;
using Trag.Application.Live;
using Trag.Application.Ports.Git;
using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// Turns change signals of the current session into accepted snapshots. Closing the repository or cancelling the
// consumer ends the stream quietly; a failed read backs off before the next attempt.
public sealed class LiveChangeStream(IRepositoryReader reader, WorkspaceState state, WatcherAttachment watchers)
{
    public async IAsyncEnumerable<RepositoryUpdate> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var slot = state.Current;
        if (slot?.Changes is not { } source)
            yield break;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(slot.Lifetime, ct);
        while (!linked.IsCancellationRequested)
        {
            var change = await NextChangeAsync(source, linked.Token).ConfigureAwait(false);
            if (change is null)
                yield break;
            RepositoryUpdate? update;
            try
            {
                update = await ReadUpdateAsync(slot, source, change, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                yield break;
            }
            if (update is not null)
                yield return update;
        }
    }

    private static async Task<RepositoryChange?> NextChangeAsync(IRepositoryChangeSource source, CancellationToken ct)
    {
        try
        {
            return await source.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (ct.IsCancellationRequested || error is ObjectDisposedException or System.Threading.Channels.ChannelClosedException)
        {
            return null;
        }
    }

    private async Task<RepositoryUpdate?> ReadUpdateAsync(RepositorySlot slot, IRepositoryChangeSource source,
        RepositoryChange change, CancellationToken ct)
    {
        if (!state.TryBeginRefresh(slot, out var version, ct))
        {
            source.Reconcile();
            await Task.Delay(100, ct).ConfigureAwait(false);
            return null;
        }
        try
        {
            var previous = slot.Snapshots.Snapshot!;
            var snapshot = await slot.Operations.ReadAsync(token => ReadSnapshotAsync(slot, change, token), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var accepted = await watchers.AcceptAsync(slot, version, snapshot).ConfigureAwait(false);
            if (accepted is not null)
                return new(accepted, source.IsPolling) { Change = SnapshotChange.Between(previous, accepted, change.Reconcile ? null : change.Paths) };
            source.Reconcile();
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Back off after a transient lock, removed folder or failed status read.
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            source.Reconcile();
            return state.StillCurrent(slot, version) ? new(null) : null;
        }
    }

    private Task<RepositorySnapshot> ReadSnapshotAsync(RepositorySlot slot, RepositoryChange change, CancellationToken ct)
    {
        var previous = slot.Snapshots.Snapshot!;
        if (!change.Reconcile && reader is IIncrementalRepositoryReader incremental)
            return incremental.ReadPathsAsync(previous, change.Paths, ct);
        return reader.ReadAsync(previous.Root, ct);
    }
}
