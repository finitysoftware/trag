using Trag.Domain.Blame;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Blame;

// A blamed file as the blame view shows it: the commit named on the first line of each block of lines it last changed,
// and its age among the file's commits, from the oldest to the newest, so the newest changes stand out.
public sealed class BlameRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static BlameCommit Commit(char id, int daysAgo) =>
        new(Snapshots.Id(id), "Ana", Now.AddDays(-daysAgo), "Change " + id, Snapshots.Path("a.txt"));

    private static FileBlame Blame(params BlameCommit[] commits) => new([.. commits.Select((commit, index) => new BlameLine(index + 1, "line " + (index + 1), commit))]);

    // Git tells a binary file by a NUL in its content, and blames it as if it had lines.
    [Fact]
    public void WhenALineHoldsANul_ShouldBeABinaryFile()
    {
        Assert.False(Blame(Commit('a', 1)).IsBinary);
        Assert.True(new FileBlame([new BlameLine(1, "PNG", Commit('a', 1)), new BlameLine(2, "\u0000IHDR", Commit('a', 1))]).IsBinary);
    }

    [Fact]
    public void WhenLinesComeInBlocks_ShouldNameTheCommitOnTheFirstLineOfEachBlockOnly()
    {
        var (a, b) = (Commit('a', 3), Commit('b', 1));

        var rows = BlameRows.Of(Blame(a, a, b, a), "a.txt", Now);

        Assert.Equal([true, false, true, true], rows.Select(row => row.StartsBlock));
        Assert.Equal([Snapshots.Id('a')[..7] + " Ana · 3 days ago", "", Snapshots.Id('b')[..7] + " Ana · yesterday", Snapshots.Id('a')[..7] + " Ana · 3 days ago"],
            rows.Select(row => row.Label));
    }

    // The file's own syntax, by its extension, carried from line to line as a comment that spans them.
    [Fact]
    public void WhenTheFileIsCode_ShouldColorItsSyntaxAcrossItsLines()
    {
        var a = Commit('a', 3);
        var blame = new FileBlame([new(1, "using X; /* a", a), new(2, "b */ int y;", a)]);

        var rows = BlameRows.Of(blame, "src/A.cs", Now);

        Assert.Equal([new(0, 5, SyntaxKind.Keyword), new(9, 4, SyntaxKind.Comment)], rows[0].Tokens);
        Assert.Equal([new(0, 4, SyntaxKind.Comment), new(5, 3, SyntaxKind.Keyword)], rows[1].Tokens);
        Assert.All(BlameRows.Of(blame, "notes.txt", Now), row => Assert.Empty(row.Tokens));
    }

    // Past the budget the diff keeps for its colors the rest of a huge file stays plain, so it opens as fast as it reads.
    [Fact]
    public void WhenTheFileOutgrowsTheColorBudget_ShouldLeaveTheRestPlain()
    {
        var a = Commit('a', 3);
        var blame = new FileBlame([new(1, "int a;", a), new(2, new string('x', SyntaxBudget.TokenLimit + 1), a), new(3, "int b;", a)]);

        var rows = BlameRows.Of(blame, "A.cs", Now);

        Assert.Equal([1, 0, 0], rows.Select(row => row.Tokens.Length));
    }

    [Fact]
    public void WhenCommitsAreOfDifferentAges_ShouldRankThemFromTheOldestToTheNewest()
    {
        var (old, middle, recent) = (Commit('a', 300), Commit('b', 40), Commit('c', 2));

        Assert.Equal([0, 2, 4, 0], BlameRows.Of(Blame(old, middle, recent, old), "a.txt", Now).Select(row => row.Age));
        Assert.Equal([BlameRows.Newest], BlameRows.Of(Blame(old), "a.txt", Now).Select(row => row.Age));
    }

    [Fact]
    public void WhenALineIsNotCommitted_ShouldSaySoAndCountItAsTheNewest()
    {
        var local = new BlameCommit(new string('0', 40), "Not Committed Yet", Now, "Version of a.txt from a.txt", Snapshots.Path("a.txt"));

        var row = BlameRows.Of(Blame(Commit('a', 3), local), "a.txt", Now)[1];

        Assert.Equal((BlameText.NotCommitted, BlameRows.Newest), (row.Label, row.Age));
    }
}
