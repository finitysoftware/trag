namespace Trag.Domain.Sync;

public static class AutoFetchIntervals
{
    // A saved number of minutes as an interval: one of the four, any other as every minute.
    public static AutoFetchInterval From(int minutes) => Enum.IsDefined((AutoFetchInterval)minutes) ? (AutoFetchInterval)minutes : AutoFetchInterval.OneMinute;
}
