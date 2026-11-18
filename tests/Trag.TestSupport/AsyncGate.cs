namespace Trag.TestSupport;

// Lets a fake pause inside an operation: the test awaits Started, inspects state, then calls Open or Cancel.
// The token is only recorded (never observed) so tests can prove a late result is discarded after cancellation.
// Open releases the calls waiting right now and closes behind them, and both the arrival and the token of every
// call are kept, so one gate can hold call after call and a test can still name the one it held.
public sealed class AsyncGate
{
    private readonly List<TaskCompletionSource> arrivals = [];
    private readonly List<CancellationToken> tokens = [];
    private TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // False by default: the gate is transparent until a test closes it.
    public bool IsClosed { get; set; }
    public int Entries { get; private set; }
    public Task Started => Arrival(1);
    public CancellationToken Token => Entries == 0 ? default : TokenAt(Entries);
    public CancellationToken FirstToken => TokenAt(1);

    // Completes once the gate has been entered for the count-th time, whether it was closed then or not: a test that
    // lets the first call through and holds the second one waits on Arrival(2).
    public Task Arrival(int count) => Pending(count).Task;

    // The token the count-th call carried; none until that call arrives.
    public CancellationToken TokenAt(int count)
    {
        lock (arrivals) return count <= tokens.Count ? tokens[count - 1] : default;
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var waiting = release;
        lock (arrivals) { Entries++; tokens.Add(cancellationToken); }
        Pending(Entries).TrySetResult();
        if (IsClosed) await waiting.Task.ConfigureAwait(false);
    }

    // Opening leaves the gate transparent, as a gate that was never closed: a test that wants to hold the next call
    // too closes it again. Anything already waiting is released, and what comes after waits on a fresh release.
    public void Open()
    {
        IsClosed = false;
        var waiting = release;
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.TrySetResult();
    }
    public void Cancel() => release.TrySetCanceled(Token);
    public void Fail(Exception error) => release.TrySetException(error);

    private TaskCompletionSource Pending(int count)
    {
        lock (arrivals)
        {
            while (arrivals.Count < count) arrivals.Add(new(TaskCreationOptions.RunContinuationsAsynchronously));
            return arrivals[count - 1];
        }
    }
}
