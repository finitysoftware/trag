using Trag.Domain.History;
using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// The search or history page the user asked for, and what the page says about it.

public sealed record HistoryFilter(string Text, bool AllBranches, bool IsSearch)
{
    // The one file whose history is shown, when the feed follows a file.
    public string? Path { get; init; }
    public string LoadingMessage => IsSearch ? "Searching the whole branch…" : "Loading history…";
    public string ValidationMessage => HistoryQuery.IsValidText(Text) ? "" : "Use a query of at most " + Counted.Grouped(GitLimits.QueryLength) + " characters without NUL.";

    public HistoryQuery ForPage(int number, int pages = 1) => new(Text, number, AllBranches) { Path = Path, Pages = pages };

    public string Describe(HistoryPage page, bool unborn)
    {
        if (page.Commits.Count == 0) return unborn && !IsSearch && Path is null ? "This branch has no commits yet." : EmptyMessage();
        if (IsSearch) return "Matches SHA, full message or author · " + Scope;
        if (Path is { } path) return "History of " + path + " on " + Scope + " · newest commits first";
        return (AllBranches ? "All branches" : "Current branch") + " · newest commits first";
    }

    private string Scope => AllBranches ? "all branches" : "the current branch";
    private string EmptyMessage() => Path is { } path ? "No commits on " + Scope + " change " + path + "." : (IsSearch ? "No commits match this query on " : "No more commits on ") + Scope + ".";
}
