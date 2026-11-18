using System.Globalization;
using Trag.Domain.Details;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the Repository details dialog says about a repository, its current branch's history, its people and what it holds.
public sealed class RepositoryOverviewTests
{
    private static readonly DateTimeOffset First = new(2025, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Latest = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static RepositoryDetails Details(BranchHistory? history = null, RepositoryContents? contents = null, params Contributor[] people) =>
        new(history ?? new(1033, 1250, First, Latest), people, contents ?? RepositoryContents.None);

    private static string Day(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void WhenTheBranchHasCommits_ShouldNameTheRepositoryItsFolderBranchAndHistory()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create(root: "D:/Repos/trag", branch: "main"), Details());

        Assert.Equal(("trag", "D:/Repos/trag", "main"), (overview.Name, overview.Path, overview.Branch));
        Assert.Equal("1,033 commits on main · 1,250 on all branches", overview.Commits);
        Assert.Equal("First commit " + Day(First) + " · latest " + Day(Latest), overview.Dates);
    }

    // A detached HEAD has no branch to count on; one commit is one.
    [Fact]
    public void WhenHeadIsDetachedAtTheOnlyCommit_ShouldCountUpToThatCommit()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create(branch: null), Details(new(1, 1, First, First)));

        Assert.Equal("1 commit up to this commit · 1 on all branches", overview.Commits);
    }

    [Fact]
    public void WhenTheBranchHasNoCommits_ShouldSaySoWithoutDates()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create(unborn: true), Details(BranchHistory.None));

        Assert.Equal(("No commits yet", ""), (overview.Commits, overview.Dates));
    }

    [Fact]
    public void WhenPeopleWroteTheBranch_ShouldGiveEachTheirCommits()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create(), Details(null, null,
            new(new("Ana Anić", "ana@example.invalid"), 1234), new(new("José", "jose@example.invalid"), 1)));

        Assert.Equal([("Ana Anić", "ana@example.invalid", "1,234 commits"), ("José", "jose@example.invalid", "1 commit")],
            overview.People.Select(person => (person.Identity.Name, person.Identity.Email, person.Commits)));
    }

    // Two columns of four: the refs and stashes the repository holds, then how it is stored and checked out.
    [Fact]
    public void WhenTheRepositoryHoldsThings_ShouldListEachWithItsValueInTwoColumns()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create() with { LfsInstalled = true }, Details(contents: new(12, 1500, 4, 2, 1, 23, 47_400_000, false, 0)));

        Assert.Equal([[("Branches", "12 local · 1,500 remote"), ("Tags", "4"), ("Stashes", "2"), ("Submodules", "1")],
            [("Git LFS", "23 files"), ("Size on disk", "45.2 MB in the Git folder"), ("History", "Complete"), ("Worktrees", "Only this one")]],
            overview.FactColumns.Select(column => column.Facts.Select(fact => (fact.Label, fact.Value)).ToArray()));
    }

    // Git LFS files where the filter is not set up, a shallow clone and other worktrees each say so.
    [Fact]
    public void WhenTheRepositoryIsUnusual_ShouldSayHow()
    {
        var overview = RepositoryOverview.Of(Snapshots.Create(), Details(contents: new(1, 0, 0, 0, 0, 1, 512, true, 2)));
        var facts = overview.FactColumns.SelectMany(column => column.Facts).ToDictionary(fact => fact.Label, fact => fact.Value);

        Assert.Equal(("1 file · Git LFS is not set up here", "Shallow clone: older commits are not here", "2 more"), (facts["Git LFS"], facts["History"], facts["Worktrees"]));
        Assert.Equal("Not used", RepositoryOverview.Of(Snapshots.Create(), Details()).FactColumns.SelectMany(column => column.Facts).Single(fact => fact.Label == "Git LFS").Value);
    }
}
