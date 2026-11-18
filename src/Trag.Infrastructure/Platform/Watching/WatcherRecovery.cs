using Trag.Domain.Presentation.Text;
namespace Trag.Infrastructure.Platform.Watching;

// What happens when the filesystem watchers cannot be trusted: one failed to start, or the OS dropped events of one. While
// that lasts the status is re-read every five seconds, and the shell can say that it is polling. Watching that could not
// start never recovers. A watcher that dropped events (npm install, a build or an unzip writes more at once than the OS
// buffers) goes on watching: what it missed is read again at once, and once it reports a change again it is trusted again.
internal sealed class WatcherRecovery : IDisposable
{
    private readonly object sync = new();
    private readonly Action reconcile;
    private readonly Timer timer;
    // The watchers that dropped events and have reported nothing since.
    private readonly HashSet<object> dropped = [];
    private bool lost;
    private volatile bool polling;
    private bool disposed;

    public bool IsPolling => polling;

    public WatcherRecovery(Action reconcile)
    {
        this.reconcile = reconcile;
        timer = new(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    // Watching could not start: the status is polled for as long as the repository is open.
    public void Start()
    {
        lock (sync)
        {
            if (disposed) return;
            lost = true;
            Poll();
        }
        reconcile();
    }

    public void Dropped(object watcher)
    {
        lock (sync)
        {
            if (disposed) return;
            dropped.Add(watcher);
            Poll();
        }
        reconcile();
    }

    // A watcher reported a change: one that had dropped events is trusted again, and polling ends once none is left.
    public void Heard(object watcher)
    {
        lock (sync)
        {
            if (disposed || !dropped.Remove(watcher) || lost || dropped.Count != 0) return;
            polling = false;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void Poll()
    {
        polling = true;
        timer.Change(TimeSpan.FromSeconds(LiveRefreshText.PollSeconds), TimeSpan.FromSeconds(LiveRefreshText.PollSeconds));
    }

    private void Tick()
    {
        lock (sync) { if (disposed || !polling) return; }
        reconcile();
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        timer.Dispose();
    }
}
