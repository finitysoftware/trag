using System.Globalization;

namespace Trag.Domain.Presentation.Text;

// The two ways a time is shown: how long ago it was, or the clock with the day when it is not today. One place for
// both, so the catalog, the stash list and the sync panel agree. Now is given; the local day is the viewer's.
public static class RelativeTime
{
    public static string Describe(DateTimeOffset time, DateTimeOffset now)
    {
        var elapsed = now - time;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return Plural((int)elapsed.TotalMinutes, "minute");
        if (elapsed < TimeSpan.FromDays(1)) return Plural((int)elapsed.TotalHours, "hour");
        if (elapsed < TimeSpan.FromDays(2)) return "yesterday";
        if (elapsed < TimeSpan.FromDays(14)) return Plural((int)elapsed.TotalDays, "day");
        if (elapsed < TimeSpan.FromDays(60)) return Plural((int)(elapsed.TotalDays / 7), "week");
        if (elapsed < TimeSpan.FromDays(365)) return Plural((int)(elapsed.TotalDays / 30), "month");
        return Plural((int)(elapsed.TotalDays / 365), "year");
    }

    public static string Opened(DateTimeOffset time, DateTimeOffset now)
    {
        var (local, today) = Local(time, now);
        if (local.Date == today) return "Today " + Clock(local);
        if (local.Date == today.AddDays(-1)) return "Yesterday " + Clock(local);
        return Stamp(local);
    }

    // A fetch line needs only the clock today, and the whole stamp otherwise.
    public static string Fetched(DateTimeOffset time, DateTimeOffset now)
    {
        var (local, today) = Local(time, now);
        return local.Date == today ? Clock(local) : Stamp(local);
    }

    private static (DateTimeOffset Local, DateTime Today) Local(DateTimeOffset time, DateTimeOffset now) =>
        (time.ToLocalTime(), now.ToLocalTime().Date);
    private static string Clock(DateTimeOffset local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);
    // A time in the viewer's zone, with the day; or the day alone.
    public static string LocalStamp(DateTimeOffset time) => Stamp(time.ToLocalTime());
    public static string LocalDay(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Stamp(DateTimeOffset local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string Plural(int count, string unit) => Counted.Of(count, unit) + " ago";
}
