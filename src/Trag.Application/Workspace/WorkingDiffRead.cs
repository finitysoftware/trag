using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// Captures the session and the snapshot it shows, while the workspace accepts the read.
public sealed class WorkingDiffRead
{
    private readonly WorkspaceState state;
    private readonly RepositorySlot slot;
    private readonly RepositorySnapshot shown;
    private readonly CancellationToken written;

    internal WorkingDiffRead(WorkspaceState state, RepositorySlot slot, RepositorySnapshot shown, CancellationToken written)
    {
        this.state = state;
        this.slot = slot;
        this.shown = shown;
        this.written = written;
    }

    // Any read of a working file: it runs behind the session's other operations and is dropped when the repository moved,
    // that is when a write runs or has run, or another snapshot is shown, by the time it ends.
    public async Task<T?> ReadAsync<T>(Func<string, CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(slot.Lifetime, ct);
        var result = await slot.Operations.ReadAsync(token => read(shown.Root, token), linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        return state.StillShows(slot, shown, written) ? result : null;
    }
}
