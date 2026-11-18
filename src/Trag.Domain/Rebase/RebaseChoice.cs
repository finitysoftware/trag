using Trag.Domain.Branches;

namespace Trag.Domain.Rebase;

// A rebase as the dialog chose it: the current branch, replayed onto another local or remote branch.
public sealed record RebaseChoice(BranchInfo? Onto, BranchInfo? Branch)
{
    public bool IsComplete => Onto is not null && Branch is not null && Onto.Ref != Branch.Ref;
}
