using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Rebase;

// What the repository looks like after a rebase command, read against the rebase it belongs to: the branch, the commit it
// goes onto (none, empty, from the first commit) and the one it started from, and, for a command on a stop, the stop it
// was given. Every outcome a rebase command can have is one of these questions, asked in order: nothing changed, finished,
// stopped again, or unexpected. Nothing changed comes first because a rewrite goes onto a commit the branch already has,
// so a rewrite Git never started would otherwise read as finished.
public sealed record RebaseStateCheck(string Branch, string Onto, string OrigHead, string? HeadBefore, RepositoryRebase? StopBefore)
{
    // A command on the rebase shown: its branch, onto and start, and the stop it is at.
    public static RebaseStateCheck Of(RepositorySnapshot fresh) =>
        new(fresh.Rebase!.Branch, fresh.Rebase.Onto, fresh.Rebase.OrigHead, fresh.Head, fresh.Rebase);

    private bool IsThisRebase(RepositoryRebase? rebase) =>
        rebase is { Limitation.Length: 0 } found && found.Branch == Branch && found.Onto == Onto && found.OrigHead == OrigHead;

    // The branch is back on itself with the onto commit in its history.
    public bool IsFinished(RepositorySnapshot after, bool onOnto) =>
        after.Operation == RepositoryOperation.None && after.Branch == Branch && onOnto;

    public bool IsUnchanged(RepositorySnapshot after) => StopBefore is null
        ? after.Operation == RepositoryOperation.None && after.Branch == Branch && after.Head == HeadBefore
        : after.Operation == RepositoryOperation.Rebase && IsThisRebase(after.Rebase) && after.Head == HeadBefore
          && after.Rebase!.LinesDone == StopBefore.LinesDone && after.Rebase.Stopped == StopBefore.Stopped;

    public bool IsStopped(RepositorySnapshot after) => after.HasSupportedRebase && IsThisRebase(after.Rebase);

    public bool IsAborted(RepositorySnapshot after) =>
        after.Operation == RepositoryOperation.None && after.Branch == Branch && after.Head == OrigHead;

    // details: what Git said, for the outcome's Details; failed: the command stopped on an exception. A commit Git could not
    // sign stops the rebase where it is, and that is what the outcome says.
    public GitWriteResult Judge(RepositorySnapshot after, bool onOnto, string details, bool failed, string ontoName, string refused)
    {
        var notSigned = CommitSignings.Refused(after.Signing, details) ? RebaseText.NotSigned(after.Signing) : null;
        // Skip puts the files of the stop back before it goes on, and a conflicted file Git rewrote there still reads as a
        // conflict, so a command stopped on a stop with conflicts may have changed what the repository does not show.
        if (IsUnchanged(after))
            return !failed ? GitWriteResult.Rejected(notSigned ?? refused, details)
                : after.Conflicts.Any() ? new(GitWriteOutcome.Uncertain, WriteText.Interrupted, details)
                : new(GitWriteOutcome.Cancelled, RebaseText.StoppedBeforeChange, details);
        if (IsFinished(after, onOnto)) return new(GitWriteOutcome.Succeeded, RebaseText.Rebased(Branch, ontoName), details);
        // A stop is Git's own only when Git ran to it; a Git stopped in the middle of a step left nothing to resolve.
        if (IsStopped(after))
            return failed ? new(GitWriteOutcome.Uncertain, WriteText.Interrupted, details)
                : new(GitWriteOutcome.NeedsResolution, notSigned ?? RebaseText.Stopped(after.Rebase!, after.Conflicts.Any()), details);
        return new(GitWriteOutcome.Uncertain, RebaseText.UnexpectedState, details);
    }
}
