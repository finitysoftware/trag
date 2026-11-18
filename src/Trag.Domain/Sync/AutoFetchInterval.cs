namespace Trag.Domain.Sync;

// How often Trag fetches without being asked, in minutes; Off never does.
public enum AutoFetchInterval { Off = 0, OneMinute = 1, FiveMinutes = 5, FifteenMinutes = 15 }
