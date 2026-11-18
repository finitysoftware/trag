namespace Trag.Application.Operations;

// One instance per open repository: two concurrent reads, and one lifetime that cancels them.
public sealed class OperationCoordinator : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ReadThrottle reads = new(2);

    public CancellationToken Lifetime => lifetime.Token;

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        return await reads.RunAsync(read, linked.Token).ConfigureAwait(false);
    }

    // Active readers release their throttle slot after observing cancellation; nothing here waits for them.
    public async ValueTask DisposeAsync() => await lifetime.CancelAsync().ConfigureAwait(false);
}
