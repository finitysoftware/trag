using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// Runs a read against the snapshot the caller is showing and returns null instead of a result the caller could not
// apply: the repository moved to another root, head or branch, a write started, or the session ended.
public sealed class SnapshotGuard(WorkspaceState state)
{
    // A read of a backend the workspace may lack: without one there is nothing to answer.
    public Task<T?> ReadAsync<TSource, T>(TSource? source, RepositorySnapshot expected, Func<TSource, CancellationToken, Task<T>> read, CancellationToken ct)
        where TSource : class where T : class? =>
        source is null ? Task.FromResult<T?>(null) : ReadAsync(expected, token => read(source, token), ct);

    // A value travels boxed, since the guard's null says the answer is not to be applied.
    public async Task<T?> ReadValueAsync<TSource, T>(TSource? source, RepositorySnapshot expected, Func<TSource, CancellationToken, Task<T?>> read, CancellationToken ct)
        where TSource : class where T : struct =>
        source is null ? null : await ReadAsync<object?>(expected, async token => await read(source, token), ct).ConfigureAwait(false) as T?;

    public async Task<T?> ReadAsync<T>(RepositorySnapshot expected, Func<CancellationToken, Task<T>> read, CancellationToken ct) where T : class?
    {
        if (!state.TryBeginHistoryRead(expected, ct, out var slot, out var linked))
            return null;
        using (linked)
        {
            var result = await slot.Operations.ReadAsync(read, linked.Token).ConfigureAwait(false);
            return state.StillMatches(slot, expected, linked.Token) ? result : null;
        }
    }
}
