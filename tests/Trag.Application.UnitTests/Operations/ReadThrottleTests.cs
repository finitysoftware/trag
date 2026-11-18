using Trag.Application.Operations;

namespace Trag.Application.UnitTests.Operations;

public sealed class ReadThrottleTests
{
    [Fact]
    public async Task WhenTheOnlySlotIsTaken_ShouldHoldTheNextReadBack()
    {
        var secondStarted = false;
        var (gate, first, second) = SecondBehindTheFirst(_ => { secondStarted = true; return Task.FromResult(2); });
        await Task.Delay(100); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(secondStarted);
        gate.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task WhenTheSlotFrees_ShouldRunTheWaitingRead()
    {
        var secondStarted = false;
        var (gate, first, second) = SecondBehindTheFirst(_ => { secondStarted = true; return Task.FromResult(2); });
        gate.SetResult();
        Assert.Equal(1, await first); Assert.Equal(2, await second); Assert.True(secondStarted);
    }

    [Fact]
    public async Task WhenAReadFails_ShouldHandTheFailureToTheCaller()
    {
        var throttle = new ReadThrottle(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throttle.RunAsync<int>(_ => throw new InvalidOperationException("boom"), CancellationToken.None));
    }

    [Fact]
    public async Task WhenAReadFollowsAFailedOne_ShouldFindTheSlotReleased()
    {
        var throttle = new ReadThrottle(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throttle.RunAsync<int>(_ => throw new InvalidOperationException("boom"), CancellationToken.None));
        Assert.Equal(3, await throttle.RunAsync(_ => Task.FromResult(3), CancellationToken.None));
    }

    [Fact]
    public async Task WhenAReadWaitingForASlotIsCanceled_ShouldThrow()
    {
        var (_, gate, _, waiting) = WaitingReadCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
    }

    // The canceled read took nothing while it waited.
    [Fact]
    public async Task WhenTheSlotFreesAfterAWaitingReadWasCanceled_ShouldGiveItToTheNextRead()
    {
        var (throttle, gate, running, waiting) = WaitingReadCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
        Assert.Equal(1, await running);
        Assert.Equal(4, await throttle.RunAsync(_ => Task.FromResult(4), CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheCallerPassesAToken_ShouldHandItToTheRead()
    {
        var throttle = new ReadThrottle(2);
        using var cancellation = new CancellationTokenSource();
        var seen = await throttle.RunAsync(ct => Task.FromResult(ct), cancellation.Token);
        Assert.Equal(cancellation.Token, seen);
    }

    // One slot, taken by a first read that waits at the gate; the given second read waits for the slot.
    private static (TaskCompletionSource Gate, Task<int> First, Task<int> Second) SecondBehindTheFirst(Func<CancellationToken, Task<int>> secondRead)
    {
        var throttle = new ReadThrottle(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = throttle.RunAsync(async _ => { await gate.Task; return 1; }, CancellationToken.None);
        var second = throttle.RunAsync(secondRead, CancellationToken.None);
        return (gate, first, second);
    }

    // One slot, held by a running read that waits at the gate; a second read waited for the slot and its caller canceled.
    private static (ReadThrottle Throttle, TaskCompletionSource Gate, Task<int> Running, Task<int> Waiting) WaitingReadCanceled()
    {
        var throttle = new ReadThrottle(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = throttle.RunAsync(async _ => { await gate.Task; return 1; }, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = throttle.RunAsync(_ => Task.FromResult(2), cancellation.Token);
        cancellation.Cancel();
        return (throttle, gate, running, waiting);
    }
}
