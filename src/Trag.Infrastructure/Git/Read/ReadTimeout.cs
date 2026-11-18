namespace Trag.Infrastructure.Git.Read;

// A read off the caller's thread that gives up after its limit, besides the caller's own cancellation. Running out of
// time is a TimeoutException, so it is never told as a cancel the user made.
internal static class ReadTimeout
{
    public static Task<T> RunAsync<T>(TimeSpan limit, Func<CancellationToken, Task<T>> read, CancellationToken ct) => Task.Run(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try { return await read(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
    }, ct);
}
