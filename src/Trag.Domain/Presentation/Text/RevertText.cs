namespace Trag.Domain.Presentation.Text;

// What reverting a commit says: the question before it, why it is refused before Git starts, and how it ended.
public static class RevertText
{
    public static string Title(string commitId) => "Revert commit " + GitObjectId.Short(commitId) + "?";
    public static string Question(string? branch, string subject) =>
        "A new commit on top of " + (branch ?? "the current commit") + " undoes \u201c" + subject + "\u201d. The commit itself stays in the history.";

    public const string NeedsCleanTree = "Commit or stash your changes before reverting a commit.";
    public const string MergeCommit = "Reverting a merge commit is not supported. Choose one of the commits it brought in instead.";
    public static string AlreadyUndone(string shortId) => "Nothing to revert: the changes of " + shortId + " are already undone here.";

    public static string Reverted(string shortId, string newShortId) => "Commit " + shortId + " reverted: the new commit " + newShortId + " undoes it.";
    public static string Stopped(string shortId, int conflicts) =>
        "Reverting " + shortId + " stopped at " + Counted.Of(conflicts, "conflicted file") + ". Resolve " + (conflicts == 1 ? "it" : "them") + ", then finish the revert.";
}
