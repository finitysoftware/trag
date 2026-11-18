namespace Trag.Domain.Sync;

// The upstream branches a read of the sync target offered, and the one chosen: Git's upstream until the user picks another.
public sealed record SyncBranchChoice(IReadOnlyList<SyncBranch> Branches, SyncBranch? Chosen)
{
    public static readonly SyncBranchChoice None = new([], null);
}
