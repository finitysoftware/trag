using Trag.Domain.Sync;

namespace Trag.Application.Sync;

// When Trag fetches without being asked: once the chosen interval passed since the last fetch (any fetch counts, the
// user's own included), at once when there was none yet, and when the window comes back after at least 15 seconds
// without one. A fetch that failed waits longer each time (1, 2, 5, then 10 minutes); one the remote refused for its
// credentials stops until a fetch succeeds, a push is accepted or the account changes, so a wrong sign-in is not
// repeated every minute.
public sealed class AutoFetchSchedule
{
    public static readonly TimeSpan ActivationGap = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];
    private DateTimeOffset? last;
    private DateTimeOffset failedAt;
    private int failures;

    public AutoFetchInterval Interval { get; set; } = AutoFetchInterval.OneMinute;
    public bool IsStopped { get; private set; }
    public bool IsOn => Interval != AutoFetchInterval.Off;

    public bool IsDue(DateTimeOffset now) => IsOn && !IsStopped && (failures > 0
        ? now - failedAt >= Backoff[Math.Min(failures, Backoff.Length) - 1]
        : last is not { } at || now - at >= TimeSpan.FromMinutes((int)Interval));

    public bool IsDueOnActivation(DateTimeOffset now) => IsOn && !IsStopped && failures == 0 && (last is not { } at || now - at >= ActivationGap);

    // A fetch that succeeded at that time, whoever ran it; an older report than the one known changes nothing.
    public void Succeeded(DateTimeOffset at)
    {
        if (last is { } known && at < known) return;
        last = at;
        failures = 0;
        IsStopped = false;
    }

    // The sign-in was proved or replaced without a fetch: a push the remote accepted, or a changed account for the
    // remote. The stop and the failures are over; the last fetch stands, so the next fetch is due as it would have been.
    public void Resume()
    {
        failures = 0;
        IsStopped = false;
    }

    public void Failed(DateTimeOffset now, bool refusedCredentials)
    {
        failures++;
        failedAt = now;
        if (refusedCredentials) IsStopped = true;
    }
}
