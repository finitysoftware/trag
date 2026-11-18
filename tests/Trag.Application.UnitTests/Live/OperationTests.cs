using Trag.Application.Live;

namespace Trag.Application.UnitTests.Live;

public sealed class OperationTests
{
    [Fact]
    public void WhenASecondStartComesWhileTheFirstRuns_ShouldRefuseItAtOnce()
    {
        var runs = 0;
        var (operation, _, first, second) = SecondStartWhileTheFirstRuns(() => runs++);
        Assert.True(operation.IsBusy); Assert.True(second.IsCompleted); Assert.Same(first, operation.Completion);
    }

    [Fact]
    public async Task WhenTheWorkReturns_ShouldStopBeingBusyWithoutEverRunningTheRefusedStart()
    {
        var runs = 0;
        var (operation, pending, first, _) = SecondStartWhileTheFirstRuns(() => runs++);
        pending.SetResult(); await first;
        Assert.False(operation.IsBusy); Assert.Equal(1, runs);
    }

    [Fact]
    public async Task WhenAStartComesAfterTheWorkReturned_ShouldRunIt()
    {
        var runs = 0;
        var (operation, pending, first, _) = SecondStartWhileTheFirstRuns(() => runs++);
        pending.SetResult(); await first;
        await operation.RunAsync(_ => { runs++; return Task.CompletedTask; });
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task WhenWorkRuns_ShouldOwnItsToken()
    {
        var (operation, tokens, pending, running) = Running();
        Assert.True(operation.Runs(tokens[0]));
        pending.SetResult(); await running;
    }

    // The work decides what a cancel means, so the operation stays busy until the work returns.
    [Fact]
    public async Task WhenTheOwnerCancels_ShouldCancelTheRunningWorksTokenWhileItStaysBusy()
    {
        var (operation, tokens, pending, running) = Running();
        operation.Cancel();
        Assert.True(tokens[0].IsCancellationRequested); Assert.True(operation.IsBusy); Assert.False(operation.Runs(tokens[0])); // not returned yet, but cancelled
        pending.SetResult(); await running;
    }

    [Fact]
    public async Task WhenCanceledWorkReturns_ShouldStopBeingBusy()
    {
        var (operation, _, pending, running) = Running();
        operation.Cancel();
        pending.SetResult(); await running;
        Assert.False(operation.IsBusy);
    }

    [Fact]
    public async Task WhenTheOwnerCancelsTheNextRun_ShouldReachItsToken()
    {
        var (operation, tokens, pending, running) = Running();
        operation.Cancel();
        pending.SetResult(); await running;

        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        running = operation.RunAsync(async ct => { tokens.Add(ct); await pending.Task; });
        // The next run starts with a token of its own, so only the owner's Cancel can cancel it.
        Assert.False(tokens[1].IsCancellationRequested);
        operation.Cancel();
        Assert.True(tokens[1].IsCancellationRequested);
        pending.SetResult(); await running;
    }

    // A first work waits until Pending is set; a second start came while it ran. Count is called whenever a work starts.
    private static (Operation Operation, TaskCompletionSource Pending, Task First, Task Second) SecondStartWhileTheFirstRuns(Action count)
    {
        var operation = new Operation();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = operation.RunAsync(async _ => { count(); await pending.Task; });
        var second = operation.RunAsync(_ => { count(); return Task.CompletedTask; });
        return (operation, pending, first, second);
    }

    // Work runs until Pending is set; Tokens collects the token of every run.
    private static (Operation Operation, List<CancellationToken> Tokens, TaskCompletionSource Pending, Task Running) Running()
    {
        var operation = new Operation();
        var tokens = new List<CancellationToken>();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = operation.RunAsync(async ct => { tokens.Add(ct); await pending.Task; });
        return (operation, tokens, pending, running);
    }
}
