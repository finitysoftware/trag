using System.Diagnostics;
using System.Threading.Channels;
using Trag.Application.Live;
using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Platform.Watching;

// One repository being watched: filesystem watchers on the working tree, on the Git directory, on the parent and on the
// user's own Git configuration, an accumulator that holds what changed until the reader asks, and a recovery that takes
// over when a watcher fails.
internal sealed class RepositoryChangeSource : IRepositoryChangeSource
{
    private readonly object sync = new();
    private readonly ChangeAccumulator pending = new();
    private readonly Channel<byte> wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly WatcherRecovery recovery;
    private bool disposed;
    public bool IsPolling => recovery.IsPolling;

    public RepositoryChangeSource(RepositorySnapshot repository, IReadOnlyList<string> configFiles)
    {
        var root = repository.Root;
        recovery = new(Reconcile);
        try
        {
            Add(root, true, e => Apply(WatchedPathClassifier.WorkingPath(root, e.FullPath, e.ChangeType)));
            foreach (var directory in new[] { repository.GitDirectory, repository.CommonDirectory }.OfType<string>().Distinct())
                Add(directory, true, e => Apply(WatchedPathClassifier.GitPath(directory, e.FullPath)));
            // A watcher of the root alone does not reliably report removal of that root. The root's own time changes with
            // every entry added to it, which reports itself.
            if (Directory.GetParent(root) is { } parent)
                Add(parent.FullName, false, e => { if (e.ChangeType != WatcherChangeTypes.Changed) Reconcile(); }, Path.GetFileName(root));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { recovery.Start(); }
        // A configuration folder that does not exist is not watched, and one that cannot be watched leaves the
        // repository's own watching as it is.
        foreach (var file in configFiles)
        {
            try { if (Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder)) Add(folder, false, _ => Reconcile(), Path.GetFileName(file)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        Reconcile(); // Close the gap between initial status and subscription.
    }

    private void Add(string directory, bool recursive, Action<FileSystemEventArgs> changed, string filter = "*")
    {
        var watcher = new FileSystemWatcher(directory, filter) { IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes };
        watchers.Add(watcher);
        // Every change the watcher reports also says it is watching again, if it had dropped events.
        void Reported(FileSystemEventArgs e)
        {
            recovery.Heard(watcher);
            changed(e);
        }
        watcher.Changed += (_, e) => Reported(e);
        watcher.Created += (_, e) => Reported(e);
        watcher.Deleted += (_, e) => Reported(e);
        watcher.Renamed += (_, e) =>
        {
            Reported(e);
            changed(new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(e.OldFullPath)!, Path.GetFileName(e.OldFullPath)));
        };
        watcher.Error += (_, _) => recovery.Dropped(watcher);
        watcher.EnableRaisingEvents = true;
    }

    // A reconcile is a signal without a path, so both decisions leave through the same door.
    private void Apply(WatchDecision decision)
    {
        if (decision.Action != WatchDecision.Kind.Ignore) Signal(decision.Path);
    }

    private void Signal(string? path)
    {
        lock (sync)
        {
            if (disposed) return;
            pending.Add(path, clock.Elapsed);
            wake.Writer.TryWrite(0);
        }
    }

    public void Reconcile() => Signal(null);

    // One decision per turn: something is due and is handed over, something is pending and its quiet period is waited
    // out, or nothing is pending and the next signal is awaited.
    public async Task<RepositoryChange> ReadAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan delay;
            lock (sync)
            {
                if (pending.Take(clock.Elapsed) is { } change) return change;
                delay = pending.Delay(clock.Elapsed);
            }
            if (delay == Timeout.InfiniteTimeSpan) await wake.Reader.ReadAsync(ct).ConfigureAwait(false);
            else await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        recovery.Dispose();
        foreach (var watcher in watchers) watcher.Dispose();
        wake.Writer.TryComplete();
    }
}
