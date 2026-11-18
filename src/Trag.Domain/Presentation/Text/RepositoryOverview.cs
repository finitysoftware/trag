using Trag.Domain.Catalog;
using Trag.Domain.Details;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What the Repository details dialog says: the repository by name and folder and the branch it is on as the header says
// it, the current branch's history in two lines, its people, and what the repository holds, a label and a value each, in
// two columns: the refs and stashes it holds, then how it is stored and checked out.
public sealed record RepositoryOverview(string Name, string Path, string Branch, string Commits, string Dates,
    IReadOnlyList<ContributorRow> People, IReadOnlyList<DetailColumn> FactColumns)
{
    public static RepositoryOverview Of(RepositorySnapshot snapshot, RepositoryDetails details) =>
        new(RepositoryEntry.DisplayName(snapshot.Root), snapshot.Root, RepositoryHeader.From(snapshot).BranchText,
            CommitsOf(snapshot.Branch, details.History), DatesOf(details.History),
            [.. details.People.Select(person => new ContributorRow(person.Identity, Count(person.Commits)))],
            ColumnsOf(snapshot, details.Contents));

    private static string CommitsOf(string? branch, BranchHistory history) => history.Commits == 0 ? "No commits yet"
        : Count(history.Commits) + (branch is null ? " up to this commit" : " on " + branch) + " · " + Counted.Grouped(history.AllCommits) + " on all branches";

    private static string DatesOf(BranchHistory history) =>
        history is { First: { } first, Latest: { } latest } ? "First commit " + RelativeTime.LocalDay(first) + " · latest " + RelativeTime.LocalDay(latest) : "";

    private static IReadOnlyList<DetailColumn> ColumnsOf(RepositorySnapshot snapshot, RepositoryContents contents) =>
    [
        new([
            new("Branches", Counted.Grouped(contents.LocalBranches) + " local · " + Counted.Grouped(contents.RemoteBranches) + " remote"),
            new("Tags", Counted.Grouped(contents.Tags)),
            new("Stashes", Counted.Grouped(contents.Stashes)),
            new("Submodules", Counted.Grouped(contents.Submodules))
        ]),
        new([
            new("Git LFS", contents.LfsFiles == 0 ? "Not used" : Counted.Of(contents.LfsFiles, "file") + (snapshot.LfsInstalled ? "" : " · Git LFS is not set up here")),
            new("Size on disk", Counted.Size(contents.GitSize) + " in the Git folder"),
            new("History", contents.IsShallow ? "Shallow clone: older commits are not here" : "Complete"),
            new("Worktrees", contents.Worktrees == 0 ? "Only this one" : Counted.Number(contents.Worktrees) + " more")
        ])
    ];

    private static string Count(int commits) => Counted.Grouped(commits) + (commits == 1 ? " commit" : " commits");
}
