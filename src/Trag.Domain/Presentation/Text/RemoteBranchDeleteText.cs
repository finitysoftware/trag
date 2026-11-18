namespace Trag.Domain.Presentation.Text;

// What deleting a branch on a remote says: why it is refused, what it does meanwhile, and how the remote answered. A
// branch is named as remote/name.
public static class RemoteBranchDeleteText
{
    public const string NameInvalid = "The remote branch name is not valid. Refresh the branch list.";
    public const string RemoteGone = "This remote no longer exists. Refresh the branch list.";
    public static string NotFetched(string branch, string remote) =>
        remote + "'s fetch settings do not bring in " + branch + ", so Trag cannot tell which branch of the remote it stands for. Nothing was deleted.";
    public static string RemoteUnreadable(string explanation) => "The remote could not be read. " + explanation + " Nothing was deleted.";
    public static string AlreadyGone(string branch, string remote) => branch + " no longer exists on " + remote + ". The stale local reference was removed.";
    public static string Deleting(string name, string remote) => "Deleting " + name + " on " + remote + "…";
    public static string NotStarted(string reason) => "The deletion did not start: " + reason;
    public static string NotVerified(string branch, string? explanation) => explanation is null
        ? "The deletion of " + branch + " could not be verified. Fetch to check the remote."
        : "The deletion of " + branch + " could not be verified: " + explanation + " Fetch to check the remote.";
    public static string Canceled(string branch) => "Deletion canceled. Fetch to check whether " + branch + " still exists.";
    public const string Failed = "The remote branch could not be deleted. Check the remote, network and Git credentials, then refresh.";
    public static string Deleted(string branch, string remote, string shortId) => branch + " was deleted on " + remote + ". It pointed at " + shortId + ".";
    public static string Interrupted(string branch) => "Deleting the remote branch was interrupted; " + branch + " is unchanged.";
    public static string Refused(string branch, string explanation) => "The remote refused to delete " + branch + ": " + explanation;
    public static string ChangedWhileDeleting(string branch) => branch + " changed while it was being deleted. Fetch and review it before trying again.";
}
