namespace Trag.Domain.Presentation.Text;

// The sentence a read or a write says when the only advice is to look again, once for every feature that says it.
public static class Failures
{
    public const string ListChanged = "Repository changed. Open the list again.";
    public const string DetailsChanged = "Repository changed. Open the details again.";
    public const string TimedOut = "Git did not answer in time. Refresh and try again.";
    public static readonly string ListNotRead = CouldNot("The list", "be read");
    public static readonly string DetailsNotRead = CouldNot("The repository's details", "be read");
    public static readonly string BranchesNotRead = CouldNot("Branches", "be read");
    public static readonly string BranchNotCreated = CouldNot("The branch", "be created");
    public static readonly string BranchNotDeleted = CouldNot("The branch", "be deleted");
    public static readonly string BranchNotRenamed = CouldNot("The branch", "be renamed");
    public static readonly string TagNotCreated = CouldNot("The tag", "be created");
    public static readonly string TagsNotRead = CouldNot("Tags", "be read");
    public static readonly string TagNotDeleted = CouldNot("The tag", "be deleted");
    public static readonly string CommitNotReverted = CouldNot("The commit", "be reverted");
    public static readonly string CommitNotCherryPicked = CouldNot("The commit", "be cherry-picked");
    public static readonly string BranchNotReset = CouldNot("The branch", "be reset");
    public static readonly string CommitsNotEdited = CouldNot("The commits", "be edited");
    public static readonly string OperationNotAborted = CouldNot("The operation", "be aborted");
    public static readonly string CommitNotCheckedOut = CouldNot("The commit", "be checked out");
    public static readonly string ChangesNotDiscarded = CouldNot("The changes", "be discarded");
    public static readonly string StashesNotRead = CouldNot("Stashes", "be read");
    public static readonly string StashNotDropped = CouldNot("The stash", "be dropped");
    public static readonly string ChangesNotStashed = CouldNot("The changes", "be stashed");
    public static readonly string PublishNotStarted = CouldNot("Publishing", "start");
    public static readonly string MergePreviewNotRead = CouldNot("What the merge would do", "be read");
    public static readonly string MergeNotStarted = CouldNot("The merge", "start");
    public static readonly string RebasePreviewNotRead = CouldNot("What the rebase would do", "be read");
    public static readonly string RebaseNotStarted = CouldNot("The rebase", "start");
    public static readonly string ConflictNotReopened = CouldNot("The conflict", "come back");
    public static readonly string ConflictNotRead = CouldNot("The conflicted file", "be read");
    public static readonly string PushNotStarted = CouldNot("The push", "start");
    public static readonly string FetchNotStarted = CouldNot("The fetch", "start");
    public static readonly string UpstreamNotRead = CouldNot("The upstream", "be read");

    public static string SwitchNotStarted(string branch) => CouldNot("The switch to " + branch, "start");
    public static string CouldNot(string what, string done) => what + " could not " + done + ". Refresh and try again.";
}
