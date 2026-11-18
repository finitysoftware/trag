namespace Trag.Domain.Presentation.Text;

// What pushing a tag to a remote, or deleting it there, says: why it is refused, what it does meanwhile, and how the
// remote answered.
public static class RemoteTagText
{
    public const string NameInvalid = "The tag name is not valid. Refresh and try again.";
    public static string Missing(string name) => "Tag " + name + " no longer exists in this repository. Refresh and try again.";
    public const string RemoteGone = "This remote no longer exists. Refresh and try again.";
    public static string RemoteUnreadable(string explanation) => "The remote could not be read. " + explanation + " Nothing was changed.";
    public static string Different(string name, string remote) => remote + " has a different tag named " + name + ". Delete one of them or choose another name.";
    public static string AlreadyThere(string name, string remote) => "Tag " + name + " is already on " + remote + ".";
    public static string AlreadyGone(string name, string remote) => "Tag " + name + " is not on " + remote + "; nothing was deleted there.";

    public static string Pushing(string name, string remote) => "Pushing tag " + name + " to " + remote + "…";
    public static string Deleting(string name, string remote) => "Deleting tag " + name + " on " + remote + "…";

    public static string Pushed(string name, string remote) => "Tag " + name + " pushed to " + remote + ".";
    public static string Deleted(string name, string remote) => "Tag " + name + " deleted on " + remote + ".";
    public static string NotStarted(string reason) => "Nothing was sent to the remote: " + reason;
    public static string Refused(string name, string remote, string explanation) => remote + " refused tag " + name + ": " + explanation;
    public static string Interrupted(string name, string remote) => "Sending tag " + name + " to " + remote + " was interrupted; " + remote + " is unchanged.";
    public static string ChangedMeanwhile(string name, string remote) => "Tag " + name + " changed on " + remote + " meanwhile. Fetch and review it before trying again.";
    public static string NotVerified(string name, string remote, string? explanation) => explanation is null
        ? "Tag " + name + " on " + remote + " could not be verified. Fetch to check the remote."
        : "Tag " + name + " on " + remote + " could not be verified: " + explanation + " Fetch to check the remote.";
    public static string Canceled(string name, string remote) => "Canceled. Fetch to check tag " + name + " on " + remote + ".";
    public const string Failed = "The tag could not be sent to the remote. Check the remote, network and Git credentials, then refresh.";
}
