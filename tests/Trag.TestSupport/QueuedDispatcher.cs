using Trag.Application.Ports.Threading;

namespace Trag.TestSupport;

// Runs every action inline until a test asks it to queue; queued actions run in order on RunAll. With it a test proves
// that a UI hop is still pending, and that a hop which runs after its shell was disposed does nothing.
public sealed class QueuedDispatcher : IViewDispatcher
{
    private readonly Queue<(Action Action, TaskCompletionSource Completion)> pending = new();
    public bool Queue { get; set; }
    public int Pending => pending.Count;

    public Task InvokeAsync(Action action)
    {
        if (!Queue)
        {
            action();
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Enqueue((action, completion));
        return completion.Task;
    }

    public void RunAll()
    {
        while (pending.TryDequeue(out var item))
        {
            item.Action();
            item.Completion.SetResult();
        }
    }
}
