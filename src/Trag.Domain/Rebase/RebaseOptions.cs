using Trag.Domain.Branches;

namespace Trag.Domain.Rebase;

// The branches a rebase dialog offers to replay the current branch onto: any other local branch, then the remote ones.
public static class RebaseOptions
{
    public static IReadOnlyList<BranchInfo> Onto(BranchCatalog catalog) => [.. catalog.Local, .. catalog.Remote];
}
