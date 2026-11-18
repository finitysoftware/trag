using Trag.Domain.Status;
namespace Trag.Domain.Presentation.Text;

// What a merge Trag runs says about how it ended, per kind: the merge half of a pull speaks of the pull, a branch merge
// the user chose speaks of the merge. Changed is the refusal when the repository is no longer the one the merge started
// from. What both kinds say the same way, and what a branch merge is refused with before Git starts, is shared.
public sealed record MergeOutcomeText(string FastForward, string MergeCommit, string Pending, string Review, string Stopped,
    string Unsynchronized, string Changed)
{
    public static readonly MergeOutcomeText Pull = new(
        "Pull completed as a fast-forward. No merge commit was created.",
        "Pull completed with a merge commit. Both parents and the staged tree were verified.",
        "Pull left a merge in progress. Resolve and stage conflicts, then finish the merge; if no conflicts remain, review the hook or signing output and retry Finish merge.",
        "Pull stopped with changes that need review. Check HEAD and the working tree before continuing.",
        "Pull stopped; verification found the branch and working tree unchanged.",
        "The pull outcome or index could not be synchronized. Review HEAD and the working tree before continuing.",
        "Fetch completed, but the branch, index, working tree or remote changed. Review the refreshed state before pulling again.");

    public static readonly MergeOutcomeText Branch = new(
        "Merge completed as a fast-forward. No merge commit was created.",
        "Merge completed with a merge commit. Both parents and the staged tree were verified.",
        "Merge left conflicts to resolve. Resolve and stage each file, then finish the merge; if no conflicts remain, review the hook or signing output and retry Finish merge.",
        "Merge stopped with changes that need review. Check HEAD and the working tree before continuing.",
        "Merge stopped; verification found the branch and working tree unchanged.",
        "The merge outcome or index could not be synchronized. Review HEAD and the working tree before continuing.",
        "The repository changed before merge. Review it and retry.");

    public const string Verifying = "Verifying merge state, commit parents and index…";
    public const string FastForwardAfterInterruption = "Fast-forward completed and was verified after interruption.";
    public const string MergeCommitAfterInterruption = "Merge commit completed and was verified after interruption.";
    public const string Rejected = "Merge was rejected. The branch and working tree are unchanged.";
    public const string IgnoredInTheWay = "The merge would overwrite ignored files in the working tree, so nothing was merged. Move the files listed below out of the way and try again.";

    // What the incoming side holds that Trag does not merge; operation is "pull" or "merge".
    public const string LfsNotDownloaded = "Git LFS could not download the content of the files this merge brings in, so nothing was merged. Git LFS's details are below.";
    public static string IncomingLfs(string operation) => "The incoming commits contain Git LFS files, and Git LFS is not set up for Git. Install Git LFS if it is missing, run git lfs install, then " + operation + " again.";

    public const string NeedsBranch = "Switch to a branch before merging into it.";
    public const string NeedsCleanTree = "Merge needs a clean working tree and index. Commit or stash your changes first.";
    public const string CommitGone = "The commit to merge is no longer available. Review the branches and try again.";
    public static string AlreadyContains(string? branch, string from) => branch + " already contains " + from + ". Nothing was merged.";

    // Aborting a merge in progress.
    public const string AbortNotVerified = "The abort outcome could not be verified. Review HEAD, merge state and working files before continuing.";
    public const string AbortIncomplete = "Merge abort did not complete verifiably. Review the refreshed files and merge state in your Git tools before continuing.";
    public const string AbortedAfterInterruption = "Merge abort completed and was verified after interruption.";
    public const string Aborted = "Merge aborted. Git restored the pre-merge index; review the remaining working files.";

    public static string NotSigned(CommitSigning signing) => CommitOutcomeText.NotSigned(signing, "The merge is ready: finish it once the key can sign, or abort it.");
}
