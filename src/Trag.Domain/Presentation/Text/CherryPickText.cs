namespace Trag.Domain.Presentation.Text;

// What cherry-picking a commit says: the question before it, why it is refused before Git starts, and how it ended.
public static class CherryPickText
{
    public static string Title(string commitId) => "Cherry-pick commit " + GitObjectId.Short(commitId) + "?";
    public static string Question(string? branch, string subject) =>
        "A new commit on top of " + (branch ?? "the current commit") + " applies “" + subject + "” again. The original commit stays where it is.";

    public const string NeedsCleanTree = "Commit or stash your changes before cherry-picking a commit.";
    public const string MergeCommit = "Cherry-picking a merge commit is not supported. Choose one of the commits it brought in instead.";
    public static string AlreadyOnBranch(string shortId, string? branch) =>
        "Commit " + shortId + " is already on " + (branch ?? "the current commit") + ". To bring back a reverted commit, revert its revert.";
    public static string NothingToApply(string shortId) => "Nothing to apply: the changes of " + shortId + " are already here.";

    public static string Picked(string shortId, string newShortId) => "Commit " + shortId + " cherry-picked: the new commit " + newShortId + " applies it.";
    public static string Stopped(string shortId, int conflicts) =>
        "Cherry-picking " + shortId + " stopped at " + Counted.Of(conflicts, "conflicted file") + ". Resolve " + (conflicts == 1 ? "it" : "them") + ", then finish the cherry-pick.";
}
