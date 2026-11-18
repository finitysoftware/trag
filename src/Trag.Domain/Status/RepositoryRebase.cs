using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;

namespace Trag.Domain.Status;

// A rebase in progress as Git's merge backend keeps it: the branch being rebased, the commit it goes onto and the one it
// started from, how many lines of its plan Git has done, the commits already replayed and those still waiting, the commit
// it stopped at and that commit's message, and a fingerprint over every file Git wrote for it, so a change made outside
// Trag is visible. A rebase Trag cannot continue says why in Limitation.
public sealed record RepositoryRebase(string Branch, string Onto, string OrigHead, int LinesDone,
    IReadOnlyList<RebaseCommit> Done, IReadOnlyList<RebaseCommit> Todo, string? Stopped, string Message, string Fingerprint, string Limitation)
{
    // The branch or remote branch the onto commit is, when one points at it; otherwise its short id.
    public string? OntoName { get; init; }
    // Git stopped for an edit command rather than a conflict: the commit is made and waits to be amended.
    public bool StoppedForEdit { get; init; }
    // Git could not commit a fold, as when the key cannot sign it, and left its changes staged.
    public bool FoldNotCommitted { get; init; }
    // Git put that fold back at the top of the plan, as it does when a file stands in its way, and up to 2.55 also when
    // the commit failed; from 2.56 a failed commit leaves the fold done and stops to amend it.
    public bool FoldPutBack { get; init; }
    // A fold put back so that gets its message a second time when Git folds it again: a squash, or a fixup -C or -c after
    // a squash.
    public bool FoldRepeatsMessage { get; init; }
    // A fold put back whose plan was changed outside Trag, as with git rebase --edit-todo, so that it no longer folds that
    // commit first: the commit dropped, picked or its line removed.
    public bool FoldPlanChanged { get; init; }

    // A rebase from the first commit goes onto nothing: Onto is empty.
    public string OntoText => Onto.Length == 0 ? RebaseText.HistoryStart : OntoName ?? GitObjectId.Short(Onto);
    // The commit Git stopped at, the last one it took from the plan.
    public RebaseCommit? Current => Done.Count == 0 ? null : Done[^1];
    // The commits still to come; a fold Git put back is the commit it stopped at, while the plan still folds it first.
    public IReadOnlyList<RebaseCommit> Waiting => [.. Todo.Skip(FoldPutBack && !FoldPlanChanged ? 1 : 0)];
    // The step and the steps counted in commits, as the plan shows them: LinesDone counts exec and break lines too.
    public int Step => Done.Count;
    public int Total => Done.Count + Waiting.Count;
    // The commits before the one it stopped at are replayed already.
    public int Applied => Math.Max(0, Step - 1);

    // At an edit stop the commit is made already: a message other than its own, as Git keeps messages, is one Continue
    // gives it. At a conflict Git makes the commit itself, from the message Trag leaves for it.
    public bool Rewords(string message) => StoppedForEdit && MessageLines.ForGit(message).TrimEnd() != MergeMessages.Clean(Message);
}
