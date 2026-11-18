namespace Trag.Application.Operations;

// At most `limit` reads run at once; a read that observes cancellation still releases its slot.
public sealed class ReadThrottle(int limit)
{
    private readonly SemaphoreSlim slots = new(limit, limit);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        await slots.WaitAsync(ct).ConfigureAwait(false);
        try { return await read(ct).ConfigureAwait(false); }
        finally { slots.Release(); }
    }
}
