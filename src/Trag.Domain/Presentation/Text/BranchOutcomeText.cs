namespace Trag.Domain.Presentation.Text;

// What a branch write says: why it is refused before Git starts, how a switch, a create, a delete or a move to a commit
// ended, and what Git's refusal means by its likely cause.
public static class BranchOutcomeText
{
    public const string NameInvalid = "Enter a valid branch name: no spaces, no '..', not starting with '-' and not ending with '/' or '.lock'.";
    public static string NameTaken(string name) => "A branch named " + name + " already exists. Choose another name or switch to it.";
    public const string Gone = "This branch no longer exists. Refresh the branch list.";
    public const string StartInvalid = "The start point is not valid. Choose a branch or commit from the list.";
    public const string StartGone = "The start point is no longer available. Refresh and choose the branch or commit again.";
    public const string TrackInvalid = "The remote branch to track is not valid.";
    public const string TrackGone = "The remote branch is no longer available. Fetch and refresh the branch list.";

    public static string CreatedAndOn(string name) => "Branch " + name + " created. You are now on it.";
    public static string Switched(string name) => "Switched to " + name + ".";
    public static string Renamed(string name, string newName) => "Branch " + name + " renamed to " + newName + ".";
    public static string Created(string name, string? current) => "Branch " + name + " created. You are still on " + (current ?? "the current commit") + ".";
    public const string NotVerified = "The branch operation could not be verified. Review the current branch before continuing.";
    public const string Interrupted = "The branch operation was interrupted before anything changed.";
    public const string UnexpectedState = "The branch operation ended in an unexpected state. Review the current branch and working files before continuing.";

    // Git's refusal of a switch or a create, by its likely cause.
    public const string UntrackedInTheWay = "Git kept the current branch: untracked files listed below would be overwritten by the branch's own. Move or remove them, or stash them with \"Include untracked files\", then switch again.";
    public const string LocalChangesInTheWay = "Git kept the current branch: your local changes to the listed files would be overwritten. Commit or stash them, then switch again.";
    public const string TargetInAnotherWorktree = "Git kept the current branch: the branch you chose is checked out in another worktree. Switch that worktree to another branch or remove it, then switch again.";
    public const string NameExists = "A branch with this name already exists. Choose another name or switch to it.";
    public const string Failed = "Git could not complete the branch operation. Review the details below.";
    public const string LfsNotDownloaded = "Git LFS could not download the content of the branch's files, so you stay on the current branch. Git LFS's details are below.";

    // Deleting a local branch.
    public static string DeleteCurrent(string name) => "Switch to another branch before deleting " + name + ".";
    public static string Deleted(string name, string shortId) => "Branch " + name + " deleted. It pointed at " + shortId + ".";
    public static string DeleteInterrupted(string name) => "Deleting the branch was interrupted; " + name + " is unchanged.";
    public static string NotFullyMerged(string name) => "Branch " + name + " is not fully merged. Removing it discards commits that exist on no other branch.";
    public static string InOtherWorktree(string name) => "Branch " + name + " is checked out in another worktree. Remove that worktree first.";
    public const string DeleteFailed = "Git could not delete the branch. Review the details below.";
    public const string DeleteNotVerified = "The branch deletion could not be verified. Refresh the branch list before continuing.";

    // Moving to a commit without a branch.
    public const string CommitMissing = "Choose a commit from the history list.";
    public static string Detached(string shortId) => "Now at " + shortId + ", not on a branch.";
    public const string CheckoutNotVerified = "The checkout could not be verified. Review the current branch before continuing.";
    public const string CheckoutInterrupted = "The checkout was interrupted before anything changed.";
    public const string CheckoutUnexpected = "The checkout ended in an unexpected state. Review the current branch and working files before continuing.";
}
