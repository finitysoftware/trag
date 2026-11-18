using Trag.Application.Live;

namespace Trag.Infrastructure.Platform.Watching;

// Called under the adapter's lock. Time is explicit for deterministic debounce tests.
public sealed class ChangeAccumulator(int capacity = 512)
{
    private readonly HashSet<string> paths = new(StringComparer.Ordinal);
    private TimeSpan? first;
    private TimeSpan last;
    private bool reconcile;

    public int Count => paths.Count;

    public void Add(string? path, TimeSpan now)
    {
        first ??= now;
        last = now;
        if (reconcile)
            return;

        if (path is not null)
        {
            paths.Add(path);
            if (paths.Count <= capacity)
                return;
        }

        reconcile = true;
        paths.Clear();
    }

    public TimeSpan Delay(TimeSpan now)
    {
        if (first is not { } started)
            return Timeout.InfiniteTimeSpan;

        var quietUntil = last + TimeSpan.FromMilliseconds(300);
        var deadline = started + TimeSpan.FromSeconds(1);
        var due = quietUntil < deadline ? quietUntil : deadline;
        var remaining = due - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public RepositoryChange? Take(TimeSpan now)
    {
        if (first is null || Delay(now) > TimeSpan.Zero)
            return null;
        var result = new RepositoryChange(paths.ToArray(), reconcile);
        first = null;
        reconcile = false;
        paths.Clear();
        return result;
    }
}
