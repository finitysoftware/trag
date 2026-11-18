namespace Trag.Domain.History;

// How much history the application is willing to read at once: one page of commits, and the files of one commit.
public static class HistoryLimits
{
    public const int PageSize = 100;
    public const int FileLimit = 10_000;
    // The commits a merge or a rebase preview lists.
    public const int PreviewCommits = 5;
}
