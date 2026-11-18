using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What a rebase says wherever it shows: the sides of its conflicts and its step, the dialog and the abort question, the
// branch header while Git has HEAD detached, why it cannot start, and how each rebase command ended.
public static class RebaseText
{
    // A rebase conflict's sides: yours is the commit replayed, by its step of the plan and the commit Git stopped at; the
    // upstream carries the plan's commits already applied. This commit brings the conflict.
    // The title of the rebase's view and of its place in the sidebar.
    public const string InProgress = "Rebase in progress";
    public const string YoursWord = "yours";
    public const string UpstreamWord = "upstream";
    public const string Change = "this commit";
    public static string Step(int step, int total) => "commit " + Counted.Number(step) + " of " + Counted.Number(total);
    public static string Stop(string step, string shortId, string subject) => step + " · " + shortId + " " + subject;
    public static string Upstream(string onto, int applied) => onto + applied switch { 0 => "", 1 => " + commit 1", var count => " + commits 1–" + count };

    // The question before the rebase in progress is aborted.
    public const string AbortTitle = "Abort this rebase?";
    public const string AbortQuestion = "Git puts the branch back where it was before the rebase. The commits replayed so far and the resolutions of this commit are dropped; the branch's own commits stay as they were.";

    public const string DialogTitle = "Rebase a branch";
    public const string DialogSubtitle = "Replay the commits of one branch on top of another.";

    public const string HistoryStart = "the start of its history";
    public static string Rebasing(RepositoryRebase rebase) => "Rebasing " + rebase.Branch + " onto " + rebase.OntoText;

    public static string Rebased(string branch, string onto) => "Rebased " + branch + " onto " + onto + ".";
    public static string UpToDate(string branch, string onto) => branch + " already contains " + onto + ". Nothing was rebased.";
    public static string Aborted(string branch) => "Rebase aborted. " + branch + " is back where it was before the rebase.";
    public static string Stopped(RepositoryRebase rebase, bool conflicts) => rebase.StoppedForEdit
        ? "The rebase stopped at " + Step(rebase.Step, rebase.Total) + " so you can edit it."
        : conflicts ? "The rebase stopped at " + Step(rebase.Step, rebase.Total) + ". Resolve its conflicts, then continue."
        : "The rebase stopped at " + Step(rebase.Step, rebase.Total) + " without a conflict. Review Details for Git's reason, then continue.";

    // Why a rebase in progress can only be finished in the user's Git tools.
    public const string ApplyBackend = "This rebase uses Git's apply backend. Continue or abort it in your Git tools.";
    public const string DetachedStart = "This rebase started on a detached HEAD. Continue or abort it in your Git tools.";
    public const string StateUnreadable = "Git's rebase state could not be read. Continue or abort it in your Git tools.";
    public const string RebaseAutostash = "This rebase uses an autostash. Continue or abort it in your Git tools.";

    // What a rebase is refused with before Git starts, and what it says when Git did not do the step or the outcome
    // could not be confirmed.
    public const string NeedsBranch = "Switch to the branch you want to rebase first.";
    public const string NeedsCleanTree = "A rebase needs a clean working tree and index. Commit or stash your changes first.";
    public const string OntoGone = "The commit to rebase onto is no longer available. Review the branches and try again.";
    public const string NotStarted = "Git did not start the rebase. Review Details for its reason.";
    public const string NotContinued = "Git did not continue the rebase. Review Details for its reason.";
    public const string NotSkipped = "Git did not skip the commit. Review Details for its reason.";
    public const string FoldNotSkipped = "Git could not fold this commit into the one before it, and skipping it would fold it again from the plan. Continue folds it, or Abort puts the branch back as it was.";
    public const string FoldRepeatsMessage = "Git could not fold this commit into the one before it, and folding it again adds its message a second time. In a terminal, run git commit --amend --no-edit if changes are staged, then git rebase --continue, and delete the repeated message in the editor Git opens; or Abort puts the branch back as it was.";
    public const string FoldPlanChanged = "Git could not fold this commit into the one before it, and its plan was changed in a terminal since. Finish the rebase there, or Abort puts the branch back as it was.";
    public const string EditNeedsStaged = "Git continues an edit stop only once every change is staged. Stage the changes that are not staged, then continue.";
    public const string EditChangedWhileAmended = "Changes that are not staged were made while the commit was amended, as a hook may make them. Git continues an edit stop only once every change is staged: stage or discard them, then continue.";
    public const string FoldNeedsStaged = "Git folds only staged changes into the commit before. Stage or discard the changes that are not staged, then continue.";
    public const string AbortNotVerified = "The rebase abort could not be verified. Review the branch and working files in your Git tools before continuing.";
    public const string OutcomeNotVerified = "The rebase stopped or finished, but its outcome could not be verified. Review the branch in your Git tools before continuing.";
    public const string StoppedBeforeChange = "The rebase was stopped before anything changed.";
    public const string UnexpectedState = "The rebase ended in a state Trag did not expect. Review the branch and working files in your Git tools before continuing.";

    public static string NotSigned(CommitSigning signing) => CommitOutcomeText.NotSigned(signing, "The rebase stopped: continue it once the key can sign, or abort it.");
}
