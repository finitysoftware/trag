namespace Trag.Domain.Presentation.Text;

// What the tag dialogs and the tag list say: the commit a new tag goes on, where it is pushed, the rows, and the
// question before a tag is deleted.
public static class TagText
{
    public static string CreateSubtitle(string shortId, string subject) => "Create a tag at " + HistoryText.Caption(shortId, subject) + ".";
    public static string PushTo(string remote) => "Push the tag to " + remote;
    // Where Git signs every tag, each one is an annotated tag and needs its message.
    public static string MessageLabel(bool signed) => signed ? "Message" : "Message (optional)";
    public static string MessageHint(bool signed) => signed ? TagOutcomeText.NeedsMessage
        : "With a message, Git keeps an annotated tag with your name and the date; without one, a lightweight tag.";

    public const string Button = "Tags";
    public static string RowDetail(string commitId, string subject) => HistoryText.Caption(GitObjectId.Short(commitId), subject);

    public static string DeleteTitle(string name) => "Delete tag " + name + "?";
    public static string DeleteQuestion(string name, string commitId) => "Tag " + name + " goes; commit " + GitObjectId.Short(commitId) + " stays in the history.";
    public static string DeleteOn(string remote) => "Also delete it on " + remote;
    public static string DeleteOnHint(string remote) => "A tag left on " + remote + " comes back with the next fetch.";
}
