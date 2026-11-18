using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// What the automatic fetch says: its choices and explanation in Settings, the Fetch button's tooltip, and why it stopped.
public static class AutoFetchText
{
    public static IReadOnlyList<AutoFetchOption> Options { get; } =
    [
        new(AutoFetchInterval.Off, "Off"), new(AutoFetchInterval.OneMinute, "Every minute"),
        new(AutoFetchInterval.FiveMinutes, "Every 5 minutes"), new(AutoFetchInterval.FifteenMinutes, "Every 15 minutes")
    ];

    public const string Title = "Fetch automatically";
    public const string Explanation = "Trag fetches the current branch's remote in the background, and when you come back to its window, so Pull and "
        + "History show what others pushed. It never pulls or changes your files, never asks for a password, and after a sign-in failure it "
        + "stops until you fetch yourself.";
    public const string Stopped = "Automatic fetch stopped: Git could not sign in to the remote. Check the account in the sync panel or in Settings, then Fetch to resume.";

    public static AutoFetchOption Option(AutoFetchInterval interval) => Options.First(option => option.Interval == interval);

    public static string FetchTip(AutoFetchInterval interval, bool stopped) => "Fetch (Ctrl+Shift+F)" + (stopped
        ? " · automatic fetch stopped after a sign-in failure; Fetch resumes it"
        : interval == AutoFetchInterval.Off ? "" : " · Trag also fetches " + Option(interval).Label.ToLowerInvariant());
}
