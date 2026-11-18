using Trag.Domain.Rebase;

namespace Trag.Domain.Presentation.Text;

// One commit of the rebase view's plan: its place, the commit, where it is and the word the row shows for it.
public sealed record RebaseStepLine(int Number, RebaseCommit Commit, RebaseStepState State, string Note)
{
    public bool IsApplied => State == RebaseStepState.Applied;
    public bool IsCurrent => State == RebaseStepState.Current;
    public bool IsWaiting => State == RebaseStepState.Waiting;
}
