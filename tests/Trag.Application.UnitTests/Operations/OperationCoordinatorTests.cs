using Trag.Application.Operations;

namespace Trag.Application.UnitTests.Operations;

public sealed class OperationCoordinatorTests
{
    [Fact]
    public async Task WhenTwoReadsAreRunning_ShouldHoldAThirdBack()
    {
        await using var coordinator = new OperationCoordinator();
        var (gates, started, reads) = await ThreeReadsAsync(coordinator);
        await Task.Delay(100); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(started[2].Task.IsCompleted);
        foreach (var gate in gates) gate.SetResult();
        await Task.WhenAll(reads);
    }

    [Fact]
    public async Task WhenOneOfTwoRunningReadsFinishes_ShouldStartTheThirdAndEachReturnsItsOwnResult()
    {
        await using var coordinator = new OperationCoordinator();
        var (gates, started, reads) = await ThreeReadsAsync(coordinator);
        gates[0].SetResult();
        Assert.Equal(0, await reads[0]);
        await started[2].Task;
        gates[1].SetResult(); gates[2].SetResult();
        var remaining = await Task.WhenAll(reads[1], reads[2]);
        Assert.Equal(new[] { 1, 2 }, remaining);
    }

    [Fact]
    public async Task WhenTheCoordinatorIsDisposed_ShouldCancelTheRunningRead()
    {
        var coordinator = new OperationCoordinator();
        var read = coordinator.ReadAsync(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; }, CancellationToken.None);
        await coordinator.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task WhenAReadIsAskedForAfterDispose_ShouldCancelIt()
    {
        var coordinator = new OperationCoordinator();
        await coordinator.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ReadAsync(_ => Task.FromResult(1), CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheCallerCancelsARunningRead_ShouldLetTheReaderObserveIt()
    {
        await using var coordinator = new OperationCoordinator();
        var read = CanceledRead(coordinator);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task WhenAReadFollowsACanceledOne_ShouldFindItsSlotReleased()
    {
        await using var coordinator = new OperationCoordinator();
        var read = CanceledRead(coordinator);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        // Two reads held at once need both slots, so they start only when the canceled read gave its slot back.
        var (gates, _, reads) = await ThreeReadsAsync(coordinator).WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var gate in gates) gate.SetResult();
        await Task.WhenAll(reads);
        Assert.Equal(1, await coordinator.ReadAsync(_ => Task.FromResult(1), CancellationToken.None));
    }

    // Three reads asked for at once, each waiting at its own gate; the first two have started.
    private static async Task<(TaskCompletionSource[] Gates, TaskCompletionSource[] Started, Task<int>[] Reads)> ThreeReadsAsync(OperationCoordinator coordinator)
    {
        var gates = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var started = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var reads = gates.Select((gate, i) => coordinator.ReadAsync(async _ => { started[i].SetResult(); await gate.Task; return i; }, CancellationToken.None)).ToArray();
        await Task.WhenAll(started[0].Task, started[1].Task);
        return (gates, started, reads);
    }

    // A read that waits for its token, whose caller cancels it while it runs.
    private static Task<int> CanceledRead(OperationCoordinator coordinator)
    {
        using var cancellation = new CancellationTokenSource();
        var read = coordinator.ReadAsync(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; }, cancellation.Token);
        cancellation.Cancel();
        return read;
    }
}
