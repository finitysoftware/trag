using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class CommitTextTests
{
    // The line under a commit message saying what its file list is compared against (was CommitComparisonTextTests in VM).
    [Fact]
    public void WhenTheCommitHasNoParent_ShouldSayItIsComparedWithAnEmptyTree() =>
        Assert.Equal("Root commit · compared with an empty tree", CommitText.Comparison(new(Commit(), "", [])));

    [Fact]
    public void WhenTheCommitHasOneParent_ShouldSayItIsComparedWithThatParent() =>
        Assert.Equal("Compared with parent p1", CommitText.Comparison(new(Commit() with { Parents = ["p1"] }, "", [])));

    [Fact]
    public void WhenTheCommitIsAMerge_ShouldSayItIsComparedWithItsFirstParent() =>
        Assert.Equal("Merge commit · diff against first parent p1", CommitText.Comparison(new(Commit() with { Parents = ["p1", "p2"] }, "", [])));

    // Git reads the oldest commit of a shallow clone as having no parent, so it is compared with an empty tree all the same.
    [Fact]
    public void WhenTheCommitIsTheOldestOfAShallowClone_ShouldSayItsParentIsNotHereInsteadOfCallingItARootCommit() =>
        Assert.Equal("Oldest commit of this shallow clone · its parent is not here, so it is compared with an empty tree",
            CommitText.Comparison(new(Commit(), "", []) { IsShallowBoundary = true }));

    [Fact]
    public void WhenACommitIsShown_ShouldNameItsAuthorTheirEmailAndTheDate()
    {
        var commit = Commit();
        Assert.Equal("Ana <ana@example.invalid> · 2026-09-24", CommitText.Identity(commit));
    }

    [Fact]
    public void WhenTheMessageIsWithinTheDisplayLimit_ShouldShowItWhole() => Assert.Equal("short", CommitText.Message("short"));

    [Fact]
    public void WhenTheMessageIsLongerThanTheDisplayLimit_ShouldSaySearchStillUsesTheWholeMessage() =>
        Assert.EndsWith("search uses the complete message.", CommitText.Message(new string('x', 16_385)));

    [Fact]
    public void WhenTheCommitChangesNoFile_ShouldSaySo()
    {
        var commit = Commit();
        Assert.Equal("This commit has no file changes against this parent.", CommitText.Files(new(commit, "", [])));
    }

    [Fact]
    public void WhenAFileWasRenamed_ShouldShowItsOldPathBeforeItsNewOne()
    {
        var file = Renamed();
        Assert.Equal("old.cs → new.cs", CommitText.FileDescription(file));
    }

    // Both paths are escaped as a list row shows them, so a line break in the old name never breaks the row.
    [Fact]
    public void WhenARenamedFileHadALineBreakInItsOldPath_ShouldEscapeIt()
    {
        var file = Renamed() with { OriginalPath = Snapshots.Path("old\nname.cs") };
        Assert.Equal("old\\nname.cs → new.cs", CommitText.FileDescription(file));
    }

    [Fact]
    public void WhenTheCommitChangesFiles_ShouldCountThem()
    {
        var (commit, file) = (Commit(), Renamed());
        Assert.Equal("2 changed files", CommitText.Files(new(commit, "", [file, file])));
    }

    [Fact]
    public void WhenTheCommitChangesOneFile_ShouldCountItInTheSingular() =>
        Assert.Equal("1 changed file", CommitText.Files(new(Commit(), "", [Renamed()])));

    // The history read on as the list is scrolled says how many commits it shows, and nothing before the first read.
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 commit")]
    [InlineData(1250, "1,250 commits")]
    public void WhenTheHistoryShowsCommits_ShouldCountThem(int count, string expected) => Assert.Equal(expected, HistoryText.Count(count));

    [Fact]
    public void WhenHistoryShowsAllBranches_ShouldSaySoInTheScopeTip() => Assert.StartsWith("Showing all branches", HistoryText.ScopeTip(true));

    [Fact]
    public void WhenHistoryShowsTheCurrentBranch_ShouldSayItsUpstreamIsShownToo() =>
        Assert.StartsWith("Showing the current branch and its upstream", HistoryText.ScopeTip(false));

    [Fact]
    public void WhenTheWorkingChangesAreLoading_ShouldSaySo() => Assert.Equal("Loading changes…", DiffMessages.Working.Loading);

    private static HistoryCommit Commit() => new(Snapshots.HeadId, "Ana", "ana@example.invalid", "2026-09-24", "Fix");

    private static FileDiffMetadata Renamed() => new(Snapshots.Path("new.cs"), "R", "", "", "", "") { OriginalPath = Snapshots.Path("old.cs") };

    // The commit pane names the co-authors the message credits, under the author.
    [Fact]
    public void WhenTheMessageCreditsCoAuthors_ShouldNameThemAfterWith()
    {
        Assert.Equal("with Ana Anić, Marko M.", CommitText.CoAuthors([new("Ana Anić", "ana@example.invalid"), new("Marko M.", "marko@example.invalid")]));
        Assert.Equal("", CommitText.CoAuthors([]));
    }
}
