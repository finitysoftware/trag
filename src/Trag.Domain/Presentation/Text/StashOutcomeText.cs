using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// What a stash write says: why it is refused before Git starts, how a push, an apply or a drop ended, and what Git's
// refusal means by its likely cause.
public static class StashOutcomeText
{
    public const string FirstCommitNeeded = "Make the first commit before stashing.";
    public static readonly string MessageInvalid = "Use a stash message of at most " + Counted.Grouped(GitLimits.StashMessageLength) + " characters without control characters, not starting with '-'.";
    public const string Missing = "Choose a stash from the list.";
    public const string Gone = "This stash no longer exists. Open the list again.";

    public static string Stashed(bool includeUntracked) => "Stashed the changes as stash@{0}." + (includeUntracked ? "" : " Untracked files stayed in place.");
    public const string NothingToStash = "There is nothing to stash: the working tree is clean.";
    public const string OnlySubmodules = "Nothing to stash: only submodules changed, and a stash does not keep what a submodule has checked out or changed. Commit or update the submodules instead.";
    public const string OnlyUntracked = "Nothing was stashed: every change is in a file Git does not track yet. Turn on \"Include untracked files\" and stash again.";
    public const string UntrackedNotIndexable = "Nothing was stashed: Git cannot keep the untracked file named below in a stash, and stashing would delete it. Rename or move the file, or turn off \"Include untracked files\".";
    public const string NoNewStash = "Git reported success but no new stash appeared. Review the working tree before continuing.";
    // Apply runs without --index, and Git keeps only new files staged.
    public static string Applied(string reference) => "Applied " + reference + ". What was staged is unstaged now, except new files. The entry stays in the stash list until you drop it.";
    public static string AppliedWithConflicts(string reference) => "Applied " + reference + " with conflicts. Resolve them in Conflicts; the stash entry is kept.";
    public static string Dropped(string reference, string shortId) => "Dropped " + reference + " (" + shortId + "). Git keeps its commit unreferenced for a while.";
    public const string StashedBeforeStop = "The changes were stashed as stash@{0}, but Git was stopped before it finished. Review the working tree before continuing.";
    public const string PushNotVerified = "Trag could not confirm whether the changes were stashed. Open the stash list to check.";
    public const string DropNotVerified = "Trag could not confirm whether the stash was dropped. Open the stash list to check.";
    public const string ListChanged = "Another program changed the stash list while this stash was being dropped, so Git dropped a different one. Trag put that one back at the top of the list, and nothing was dropped. Drop the stash again.";

    // Git's refusal by its likely cause.
    public const string UntrackedInTheWay = "Git kept the working tree: untracked files listed below would be overwritten by files from the stash. Move or remove them, then apply again.";
    public const string LocalChangesInTheWay = "Git kept the working tree: local changes to the listed files would be overwritten. Commit or stash them first.";
    public const string FilesInTheWay = "Git applied the stash except the untracked files named below, which already exist in the working tree and stayed as they are. The stash entry is kept.";
    public const string Failed = "Git could not complete the stash operation. Review the details below.";
}
