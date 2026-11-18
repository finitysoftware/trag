namespace Trag.Domain.Details;

// What the repository's details show beside what its snapshot already says: the current branch's history, the people who
// wrote it, the one with most commits first, and what the repository holds.
public sealed record RepositoryDetails(BranchHistory History, IReadOnlyList<Contributor> People, RepositoryContents Contents)
{
    public static readonly RepositoryDetails None = new(BranchHistory.None, [], RepositoryContents.None);
}
