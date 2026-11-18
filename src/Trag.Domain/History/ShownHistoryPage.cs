using Trag.Domain.Presentation.Text;

namespace Trag.Domain.History;

// The history the model shows: the commits read so far, page after page as the list is scrolled, and their graph, the
// last page read, whether more follow, and what the count and the message say. Its name is not a view's: the screen
// binds it as it is.
public sealed record ShownHistoryPage(IReadOnlyList<HistoryCommit> Commits, CommitGraph Graph,
    int Number, bool HasMore, string CountText, string Message)
{
    public static ShownHistoryPage Empty { get; } = new([], CommitGraph.Empty, 0, false, "", HistoryText.OpenRepository);

    public ShownHistoryPage Reading(string message) => this with
    {
        Commits = [], Graph = CommitGraph.Empty, HasMore = false, CountText = "", Message = message
    };

    // The history read from its start; the layout is the one the pages read after it go on with.
    public static ShownHistoryPage From(HistoryPage page, CommitGraphLayout layout, HistoryFilter filter, bool unborn) =>
        Shown(page, page.Commits, layout, filter, unborn);

    // The next page read, below the commits shown.
    public ShownHistoryPage Then(HistoryPage next, CommitGraphLayout layout, HistoryFilter filter, bool unborn) =>
        Shown(next, [.. Commits, .. next.Commits], layout, filter, unborn);

    // A file's history lists only the commits that change the file, and a search only those that match, so no graph is
    // drawn from them.
    private static ShownHistoryPage Shown(HistoryPage read, IReadOnlyList<HistoryCommit> commits, CommitGraphLayout layout, HistoryFilter filter, bool unborn) =>
        new(commits, filter.Path is null && !filter.IsSearch ? layout.Add(read.Commits) : CommitGraph.Empty, read.Number, read.HasMore,
            HistoryText.Count(commits.Count), filter.Describe(read with { Commits = commits }, unborn));
}
