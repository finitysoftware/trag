namespace Trag.Application.Ports.Threading;

// For composition without a UI thread (tests, probes): the action runs where it is asked.
public sealed class ImmediateViewDispatcher : IViewDispatcher
{
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
}
