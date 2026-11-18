using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// A read that runs out of time fails as a timeout, never as a cancel the user did not make; the caller's own cancel stays
// a cancel.
public sealed class ReadTimeoutTests
{
    [Fact]
    public async Task WhenTheReadRunsOutOfTime_ShouldFailAsATimeout() =>
        await Assert.ThrowsAsync<TimeoutException>(() => ReadTimeout.RunAsync(TimeSpan.FromMilliseconds(20), WaitAsync, CancellationToken.None));

    [Fact]
    public async Task WhenTheCallerCancelsARunningRead_ShouldStayACancel()
    {
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = ReadTimeout.RunAsync(TimeSpan.FromMinutes(1), ct => { started.SetResult(); return WaitAsync(ct); }, cancel.Token);
        await started.Task;
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    private static async Task<int> WaitAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    }
}
