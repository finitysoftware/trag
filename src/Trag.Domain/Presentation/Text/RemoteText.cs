namespace Trag.Domain.Presentation.Text;

// What managing the remotes of a repository says: what keeps an edit back, what removing one means, and how a write ended.
public static class RemoteText
{
    public const string Gone = "That remote is no longer in this repository. Refresh and try again.";
    public const string UrlMissing = "Enter the remote's URL or path.";
    public const string UrlInvalid = "Enter a URL or path without spaces, line breaks or a leading '-'.";
    public const string UrlHasPassword = "Remove the password or token from the URL and add the account in Settings → Integrations instead.";
    public const string Failed = "Git did not change the remote. See the details below.";
    public const string NotVerified = "The remote change could not be verified. Review the remotes before continuing.";
    public const string Interrupted = "The remote change was interrupted before anything changed.";

    public static string Added(string name) => "Remote " + name + " added.";
    public static string Saved(string name) => "Remote " + name + " saved.";
    public static string SavedPushingElsewhere(string name, string push) => Saved(name) + " Pushes still go to " + push + ".";
    public static string Removal(string name, string? trackingBranch) =>
        "Removing " + name + " takes its remote-tracking branches out of this repository"
        + (trackingBranch is null ? "" : ", and " + trackingBranch + " loses its upstream") + ". Nothing is deleted on the server.";
    public static string Removed(string name) => "Remote " + name + " removed; nothing was deleted on the server.";
}
