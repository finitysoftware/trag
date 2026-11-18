using Trag.Application.Ports.Git;
using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// Opens, refreshes and closes sessions. Replacing a session cancels its reads and invalidates their results, even
// when a reader completes after cancellation has been requested.
public sealed class RepositoryOpener(IRepositoryReader reader, WorkspaceState state, WatcherAttachment watchers)
{
    public async Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken ct)
    {
        var next = state.Replace(path, out var previous);
        if (previous is not null)
            await previous.RetireAsync().ConfigureAwait(false);
        return await ReadAsync(next, ct).ConfigureAwait(false);
    }

    public Task<RepositorySnapshot?> RefreshAsync(CancellationToken ct) =>
        state.Current is { } slot ? ReadAsync(slot, ct) : Task.FromResult<RepositorySnapshot?>(null);

    public async Task CloseAsync()
    {
        if (state.Detach() is { } previous)
            await previous.RetireAsync().ConfigureAwait(false);
    }

    private async Task<RepositorySnapshot?> ReadAsync(RepositorySlot slot, CancellationToken ct)
    {
        if (!state.TryBeginRefresh(slot, out var version))
            return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(slot.Lifetime, ct);
        try
        {
            var snapshot = await slot.Operations.ReadAsync(ct => reader.ReadAsync(slot.Lease.Path, ct), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return await watchers.AcceptAsync(slot, version, snapshot).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException && (slot.Lifetime.IsCancellationRequested || !slot.Lease.IsCurrent(version)))
        {
            return null;
        }
    }
}
