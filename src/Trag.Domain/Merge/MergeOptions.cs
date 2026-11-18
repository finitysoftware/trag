using Trag.Domain.Branches;

namespace Trag.Domain.Merge;

// The branches a merge dialog offers: only a local branch can take a merge, the current one first; every branch can
// give one, the local ones and then the remote ones.
public static class MergeOptions
{
    public static IReadOnlyList<BranchInfo> Into(BranchCatalog catalog) => catalog.Current is { } current ? [current, .. catalog.Local] : catalog.Local;

    public static IReadOnlyList<BranchInfo> From(BranchCatalog catalog) => [.. Into(catalog), .. catalog.Remote];
}
