namespace Trag.Domain.Presentation.Text;

// What the status line and the notice say about watching the repository: watching, or checking on a timer without a
// watcher, a failed check that is retried, and watching that stopped.
public static class LiveRefreshText
{
    public const string Stopped = "Live refresh stopped";
    public const string Unavailable = "Automatic refresh is unavailable. Choose Refresh to reconnect.";
    public const string Retrying = "Live refresh failed · retrying";
    public const string NotChecked = "Changes could not be checked. Showing the last status; retrying automatically.";

    // How often the repository is checked while no watcher sees its changes.
    public const int PollSeconds = 5;

    public static string Watching(bool polling) => polling ? "Checking every " + PollSeconds + " seconds · watcher unavailable" : "Watching for changes";
}
