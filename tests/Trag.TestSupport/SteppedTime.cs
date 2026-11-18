namespace Trag.TestSupport;

// A clock the test moves whose timers fire once it passes their due time, as a Task.Delay on it expects; every timer
// fires once. ManualTime is the clock whose timers never fire.
public sealed class SteppedTime : TimeProvider
{
    private readonly object sync = new();
    private readonly List<PendingTimer> timers = [];
    private DateTimeOffset now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (sync) return now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new PendingTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<PendingTimer> due;
        lock (sync)
        {
            now += by;
            due = timers.Where(timer => timer.Due <= now).ToList();
            timers.RemoveAll(due.Contains);
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class PendingTimer(SteppedTime clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.sync)
            {
                clock.timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan) return true;
                Due = clock.now + dueTime;
                clock.timers.Add(this);
            }
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock.sync) clock.timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
