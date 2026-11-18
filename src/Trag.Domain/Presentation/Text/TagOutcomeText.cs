using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What creating or deleting a tag says: why it is refused before Git starts, and how it ended.
public static class TagOutcomeText
{
    public const string NameInvalid = "Enter a valid tag name: no spaces, no '..', not starting with '-' and not ending with '/' or '.lock'.";
    public static string NameTaken(string name) => "A tag named " + name + " already exists. Choose another name.";
    public const string NeedsMessage = "Tags are signed in this repository, so a tag needs a message.";

    public static string Created(string name, string shortId) => "Tag " + name + " created at " + shortId + ".";
    public const string Failed = "Git could not create the tag. Review the details below.";
    public static string NotSigned(CommitSigning key) => CommitOutcomeText.NotSigned("tag", key, "No tag was created.");
    public const string Interrupted = "Creating the tag was interrupted before anything changed.";
    public const string NotVerified = "The tag could not be verified. Refresh the history before continuing.";

    public static string Gone(string name) => "Tag " + name + " no longer exists. Refresh the tag list.";
    public static string Deleted(string name, string shortId) => "Tag " + name + " deleted. It marked " + shortId + ".";
    public const string DeleteFailed = "Git could not delete the tag. Review the details below.";
    public const string DeleteInterrupted = "Deleting the tag was interrupted before anything changed.";
    public const string DeleteNotVerified = "The tag deletion could not be verified. Refresh the tag list before continuing.";
}
