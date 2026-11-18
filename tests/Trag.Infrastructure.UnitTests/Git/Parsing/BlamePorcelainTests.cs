using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// Git's blame --porcelain as the reader parses it: a commit's fields come once and its later groups only name it, a line
// keeps no carriage return, a quoted path is undone, a commit can own lines under two paths, and output that stops
// mid-line is no blame.
public sealed class BlamePorcelainTests
{
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    private static string Porcelain() =>
        First + " 1 1 2\n" + "author Ana\n" + "author-mail <ana@example.com>\n" + "author-time 1700000000\n" + "author-tz +0100\n"
        + "committer Ana\n" + "committer-mail <ana@example.com>\n" + "committer-time 1700000000\n" + "committer-tz +0100\n"
        + "summary First\n" + "boundary\n" + "filename \"a\\\"b.txt\"\n" + "\tline one\r\n"
        + First + " 2 2\n" + "\tline two\n"
        + Second + " 3 3 1\n" + "author Bo\n" + "author-mail <bo@example.com>\n" + "author-time 1700000100\n" + "author-tz -0500\n"
        + "committer Bo\n" + "committer-mail <bo@example.com>\n" + "committer-time 1700000100\n" + "committer-tz -0500\n" + "summary Second\n"
        + "previous " + First + " \"a\\\"b.txt\"\n" + "filename \"a\\\"b.txt\"\n" + "\tline three\n"
        + First + " 5 4 1\n" + "filename other.txt\n" + "\tline four\n";

    [Fact]
    public void WhenACommitOwnsSeveralGroups_ShouldReadItsFieldsOnceAndShareTheCommit()
    {
        var lines = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(Porcelain())).Lines;

        Assert.Equal(["line one", "line two", "line three", "line four"], lines.Select(line => line.Text));
        Assert.Same(lines[0].Commit, lines[1].Commit);
        Assert.Equal(("Ana", "First", DateTimeOffset.FromUnixTimeSeconds(1700000000)), (lines[0].Commit.Author, lines[0].Commit.Summary, lines[0].Commit.Time));
    }

    [Fact]
    public void WhenAPathIsQuotedWithAnEscapedQuote_ShouldUndoTheQuoting()
    {
        var lines = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(Porcelain())).Lines;

        Assert.Equal("a\"b.txt", lines[0].Commit.Path.Display);
        Assert.Equal((First, "a\"b.txt"), (lines[2].Commit.Previous, lines[2].Commit.PreviousPath!.Display));
    }

    // Git quotes a path with a control character as C does: a letter for the seven that have one, three octal digits for
    // the rest; a byte past ASCII stays as it is under core.quotePath=false.
    [Fact]
    public void WhenThePreviousPathHasCEscapes_ShouldUnquoteThemAsGitDoes()
    {
        var output = Porcelain().Replace("\"a\\\"b.txt\"", "\"\\a\\b\\t\\n\\v\\f\\r\\001\\177\\\"\\\\\u00e9.txt\"", StringComparison.Ordinal);

        var lines = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(output)).Lines;

        const string Path = "\a\b\t\n\v\f\r\u0001\u007f\"\\\u00e9.txt";
        Assert.Equal((Path, Path, Path), (lines[0].Commit.Path.Display, lines[2].Commit.Path.Display, lines[2].Commit.PreviousPath!.Display));
    }

    // A commit whose moved lines came from another file names that path again on the group; the author stays its own.
    [Fact]
    public void WhenACommitHasGroupsUnderTwoPaths_ShouldGiveEachGroupItsOwnPath()
    {
        var lines = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(Porcelain())).Lines;

        Assert.Equal(("other.txt", "Ana", First), (lines[3].Commit.Path.Display, lines[3].Commit.Author, lines[3].Commit.Id));
        Assert.Equal("a\"b.txt", lines[0].Commit.Path.Display);
    }

    // A header without the group's length continues the group of the line before it, whatever commit it names.
    [Fact]
    public void WhenAHeaderWithoutAGroupLengthFollowsTheFirstLine_ShouldContinueTheFirstLinesGroup()
    {
        var output = Porcelain().Replace(First + " 2 2\n", Second + " 2 2\n", StringComparison.Ordinal);

        var lines = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(output)).Lines;

        Assert.Same(lines[0].Commit, lines[1].Commit);
    }

    // The commit before a group ends at the first space of its "previous" line, and the path follows it.
    [Fact]
    public void WhenThePreviousLineStartsWithASpace_ShouldReadAnEmptyCommitBeforeThePath()
    {
        var output = Porcelain().Replace("previous " + First + " ", "previous  ", StringComparison.Ordinal);

        var commit = BlamePorcelain.Parse(Encoding.UTF8.GetBytes(output)).Lines[2].Commit;

        Assert.Equal(("", "a\"b.txt"), (commit.Previous, commit.PreviousPath!.Display));
    }

    // The last second a date can hold, 9999-12-31T23:59:59Z, is still a time.
    [Fact]
    public void WhenTheTimeIsTheLastSecondADateHolds_ShouldReadIt()
    {
        var output = Porcelain().Replace("author-time 1700000100", "author-time 253402300799", StringComparison.Ordinal);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(253402300799), BlamePorcelain.Parse(Encoding.UTF8.GetBytes(output)).Lines[2].Commit.Time);
    }

    [Fact]
    public void WhenTheOutputStopsMidLine_ShouldRefuseItAsIncomplete()
    {
        var cut = Porcelain()[..^3];

        Assert.Throws<FormatException>(() => BlamePorcelain.Parse(Encoding.UTF8.GetBytes(cut)));
    }

    // Git's output with one line in a shape Git never writes: a header without the line's number or with one past an int,
    // a commit without a field Git always gives it, a previous commit without its path or with an octal escape the closing
    // quote cuts short, a time past what a date can hold.
    [Theory]
    [InlineData(" 2 2\n", " 2\n")]
    [InlineData(" 2 2\n", " 2 99999999999\n")]
    [InlineData("author Bo\n", "")]
    [InlineData("author-time 1700000100\n", "")]
    [InlineData("summary Second\n", "")]
    [InlineData("filename \"a\\\"b.txt\"\n\tline three", "\tline three")]
    [InlineData(" \"a\\\"b.txt\"\nfilename", "\nfilename")]
    [InlineData(" \"a\\\"b.txt\"\nfilename", " \"a\\03\"\nfilename")]
    [InlineData("author-time 1700000100", "author-time 999999999999")]
    [InlineData("author-time 1700000100", "author-time 99999999999999999999")]
    public void WhenALineIsNotInTheShapeGitWrites_ShouldRefuseTheOutputAsMalformed(string line, string replacement)
    {
        var malformed = Porcelain().Replace(line, replacement, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => BlamePorcelain.Parse(Encoding.UTF8.GetBytes(malformed)));
    }
}
