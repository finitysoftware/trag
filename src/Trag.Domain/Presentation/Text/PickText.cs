using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What a commit Git applies and stopped at its conflicts says, a revert or a cherry-pick alike, each named its way: its
// view and its sides, the banners, the question before it is aborted, its actions, and how finishing or aborting ended.
public static class PickText
{
    // What the operation is called in a sentence: a cherry-pick, else a revert.
    public static string Name(RepositoryOperation operation) => operation == RepositoryOperation.CherryPick ? "cherry-pick" : "revert";
    private static string Capitalized(RepositoryOperation operation) => operation == RepositoryOperation.CherryPick ? "Cherry-pick" : "Revert";

    public static string InProgress(RepositoryOperation operation) => Capitalized(operation) + " in progress";
    public static string ShallowBoundary(RepositoryOperation operation, string shortId) => "The commit before " + shortId
        + " is not in this shallow clone, so Git would take " + shortId + " for a first commit and " + (operation == RepositoryOperation.CherryPick ? "apply" : "undo")
        + " every file it holds instead of its change. Fetch the full history in Git first.";
    public static string ViewName(RepositoryOperation operation) => Name(operation) + " view";
    public static string Banner(RepositoryOperation operation, int open) => open == 0 ? "Every conflict is resolved. Finish the " + Name(operation) + " in the " + ViewName(operation) + "."
        : Counted.InConflict(open) + " Conflicts are resolved in the " + ViewName(operation) + ", not here.";
    public static string ViewLine(RepositoryOperation operation, int open) => open == 0 ? "Every conflict is resolved. Finish the " + Name(operation) + " to commit it."
        : Counted.InConflict(open) + " Resolve each, then finish the " + Name(operation) + ".";

    // The sides: the current branch is the user's own work, the commit Git applies the other side.
    public static string Change(RepositoryOperation operation) => "the " + Name(operation);
    public static string SideName(RepositoryOperation operation, string shortId) => "the " + Name(operation) + " of " + shortId;

    public static string FinishAction(RepositoryOperation operation) => "Finish " + Name(operation);
    public static string FinishHint(RepositoryOperation operation) => "Finishing commits the " + Name(operation) + " with the message Git prepared for it.";
    public static string AbortAction(RepositoryOperation operation) => "Abort " + Name(operation) + "…";
    public static string AbortTitle(RepositoryOperation operation) => "Abort this " + Name(operation) + "?";
    public static string AbortQuestion(RepositoryOperation operation) =>
        "Git puts the branch and its files back as they were before the " + Name(operation) + ". Resolutions made so far are discarded.";
    public static string KeepAction(RepositoryOperation operation) => "Keep " + Name(operation);
    public static string ConfirmAbortAction(RepositoryOperation operation) => "Abort this " + Name(operation);

    public static string Finishing(RepositoryOperation operation) => "Finishing the " + Name(operation) + "…";
    public static string Aborting(RepositoryOperation operation) => "Aborting the " + Name(operation) + " and verifying the result…";
    public static string ConflictsLeft(RepositoryOperation operation) => "Resolve and stage every conflicted file before finishing the " + Name(operation) + ".";
    public static string Finished(RepositoryOperation operation, string shortId, string newShortId) =>
        Capitalized(operation) + " of " + shortId + " finished: the new commit " + newShortId + (operation == RepositoryOperation.CherryPick ? " applies it." : " undoes it.");
    public static string Failed(RepositoryOperation operation) => "Git could not " + Name(operation) + " the commit. Review the details below.";
    public static string Interrupted(RepositoryOperation operation) => "The " + Name(operation) + " was interrupted before anything changed.";
    public static string NotFinished(RepositoryOperation operation) => "Git could not finish the " + Name(operation) + ". Review the details below.";
    public static string NotVerified(RepositoryOperation operation) =>
        "The " + Name(operation) + " could not be verified. Review the current branch and working files before continuing.";
    public static string Aborted(RepositoryOperation operation, string? branch) => Capitalized(operation) + " aborted. " + (branch ?? "HEAD") + " is back where it was.";
    public static string AbortNotVerified(RepositoryOperation operation) =>
        "Aborting the " + Name(operation) + " could not be verified. Refresh and review the current branch before continuing.";

    public static string NotSigned(RepositoryOperation operation, CommitSigning signing) =>
        CommitOutcomeText.NotSigned(signing, "The " + Name(operation) + " is ready: finish it once the key can sign, or abort it.");
    public static string NotSignedUndone(CommitSigning signing) => CommitOutcomeText.NotSigned(signing, "Nothing was changed.");

    // The same for a pick whose prepare-commit-msg hook failed after Git applied the commit.
    private const string HookRefusedCommit = "The prepare-commit-msg hook refused the commit.";
    public static string HookRefused(RepositoryOperation operation) =>
        HookRefusedCommit + " The " + Name(operation) + " is ready: finish it once the hook allows it, or abort it.";
    public const string HookRefusedUndone = HookRefusedCommit + " Nothing was changed.";
}
