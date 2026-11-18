using Trag.Application.Operations;

namespace Trag.Application.UnitTests.Operations;

public sealed class LatestRequestTests
{
    [Fact]
    public void WhenTheCallerCancels_ShouldInterruptItsRequestButKeepItTheLatest()
    {
        var request = new LatestRequest();
        using var caller = new CancellationTokenSource();
        using var first = request.Begin(caller.Token);
        caller.Cancel();
        Assert.True(first.IsLatest);
        Assert.False(first.IsCurrent);
    }

    // The caller's cancellation stays with its own scope, and that scope's late dispose does not take the next request
    // with it: an interrupt still reaches the next one.
    [Fact]
    public void WhenACallerCanceledScopeIsDisposedAfterTheNextBegan_ShouldLeaveTheNextOneTheLatestAndInterruptible()
    {
        var request = new LatestRequest();
        using var caller = new CancellationTokenSource();
        var first = request.Begin(caller.Token);
        caller.Cancel();
        using var second = request.Begin();
        first.Dispose();
        request.Interrupt();
        Assert.True(second.Token.IsCancellationRequested);
        Assert.True(second.IsLatest);
    }

    [Fact]
    public void WhenNothingHasBegun_ShouldNotBeActive()
    {
        var request = new LatestRequest();
        Assert.False(request.IsActive);
    }

    [Fact]
    public void WhenARequestBegins_ShouldBeActiveAndCurrentWithALiveToken()
    {
        var request = new LatestRequest();
        using var first = request.Begin();
        Assert.True(request.IsActive); Assert.True(first.IsCurrent); Assert.False(first.Token.IsCancellationRequested);
    }

    [Fact]
    public void WhenANewRequestBegins_ShouldCancelThePreviousOneAndMakeItStale()
    {
        var request = new LatestRequest();
        using var first = request.Begin();
        using var second = request.Begin();
        Assert.True(first.Token.IsCancellationRequested); Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent); Assert.True(request.IsCurrent(second.Ticket)); Assert.False(request.IsCurrent(first.Ticket));
    }

    [Fact]
    public void WhenAStaleScopeIsDisposed_ShouldKeepTheNewerRequestActive()
    {
        var (request, _) = NewerAfterADisposedStaleOne();
        Assert.True(request.IsActive);
    }

    [Fact]
    public void WhenTheRequestIsCanceled_ShouldEndTheCurrentOne()
    {
        var (request, second) = NewerAfterADisposedStaleOne();
        request.Cancel();
        Assert.False(request.IsActive); Assert.True(second.Token.IsCancellationRequested); Assert.False(second.IsCurrent);
    }

    [Fact]
    public void WhenTheScopeOfACanceledRequestIsDisposed_ShouldStayInactive()
    {
        var (request, second) = NewerAfterADisposedStaleOne();
        request.Cancel();
        second.Dispose();
        Assert.False(request.IsActive);
    }

    [Fact]
    public void WhenARequestIsInterrupted_ShouldCancelItsTokenButKeepItTheLatestAndActive()
    {
        var (request, scope, generation) = Interrupted();
        Assert.True(scope.Token.IsCancellationRequested); Assert.False(scope.IsCurrent);
        Assert.True(scope.IsLatest); Assert.Equal(generation, request.Generation); Assert.True(request.IsActive);
    }

    [Fact]
    public void WhenANewRequestBeginsAfterAnInterrupt_ShouldMakeTheNewOneTheLatest()
    {
        var (request, scope, generation) = Interrupted();
        using var next = request.Begin();
        Assert.False(scope.IsLatest); Assert.True(next.IsLatest); Assert.True(next.IsCurrent); Assert.Equal(generation + 1, request.Generation);
    }

    // Unlike an interrupt, a cancel supersedes the request: a newer generation begins.
    [Fact]
    public void WhenTheLatestRequestIsCanceled_ShouldMakeItStaleInANewGeneration()
    {
        var (request, _, generation) = Interrupted();
        using var next = request.Begin();
        request.Cancel();
        Assert.False(next.IsLatest); Assert.False(next.IsCurrent); Assert.Equal(generation + 2, request.Generation);
    }

    // Two requests began, and the first, superseded one has already disposed its scope.
    private static (LatestRequest Request, LatestRequest.Scope Second) NewerAfterADisposedStaleOne()
    {
        var request = new LatestRequest();
        var first = request.Begin();
        var second = request.Begin();
        first.Dispose();
        return (request, second);
    }

    // One request began and was interrupted; the generation is the one it began in.
    private static (LatestRequest Request, LatestRequest.Scope Scope, long Generation) Interrupted()
    {
        var request = new LatestRequest();
        var scope = request.Begin();
        var generation = request.Generation;
        request.Interrupt();
        return (request, scope, generation);
    }
}
