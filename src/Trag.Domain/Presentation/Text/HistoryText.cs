using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What the history says around its commits: how many it shows, the scope button, why it could not be read, and a
// commit's date.
public static class HistoryText
{
    public const string OpenRepository = "Open a repository to browse history.";
    public const string Changed = "Repository changed. Refresh to load current history.";
    public const string CommitGone = "This commit is no longer available. Refresh the history and choose it again.";
    public const string Canceled = "History loading canceled. Choose Search or History to retry.";
    public const string Failed = "History could not be read or HEAD changed. Refresh and retry; unusually large commit messages may exceed the display limit.";

    // A file's history names the path the file came from on the commit that renamed or copied it.
    public static string CameFrom(string path) => "from " + path;
    public static string Caption(string shortId, string subject) => shortId + " · " + subject;
    public static string Title(bool search) => search ? "Search" : "History";
    public static string Count(int count) => count == 0 ? "" : Counted.Grouped(count) + (count == 1 ? " commit" : " commits");

    // Git's strict ISO author date, shown with the time in the viewer's local zone; anything unexpected is shown as received.
    public static string Date(string date) => DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)
        ? RelativeTime.LocalStamp(when) : date;

    // Why a commit of the history is not cherry-picked, reverted, reset to or edited from while an operation is in
    // progress, named as the header names it, before the one action of these the question is about.
    public const string CherryPicking = "cherry-picking a commit";
    public const string Reverting = "reverting a commit";
    public const string Resetting = "resetting the branch";
    public const string Editing = "editing commits";
    public static string OperationInProgress(RepositoryOperation operation, string action) =>
        RepositoryHeader.OperationName(operation) + " in progress. Finish or abort it before " + action + ".";

    public static string ScopeTip(bool allBranches) => allBranches
        ? "Showing all branches · click to show only the current branch"
        : "Showing the current branch and its upstream · click to show all branches";
}
