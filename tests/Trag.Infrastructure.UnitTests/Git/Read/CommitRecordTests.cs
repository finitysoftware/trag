using System.Text;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// One commit as Git prints it, turned into the row the history list shows. A commit may carry a megabyte in any field,
// so the row is clipped; the full message stays on the record.
public sealed class CommitRecordTests
{
    private const string Id = "1111111111111111111111111111111111111111";
    private static CommitRecord Record(string author, string email, string message) =>
        CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0" + Id + "\0" + author + "\0" + email + "\02024-01-01T00:00:00+00:00\0" + message));

    [Fact]
    public void WhenTheMessageHasABody_ShouldShowOnlyTheSubjectWithoutItsCarriageReturn()
    {
        var summary = Record("Author č", "author@example.invalid", "Subject č\r\n\r\nBody that is not shown\n").Summary;
        Assert.Equal("Subject č", summary.Subject);
        Assert.Equal([Id], summary.Parents);
        Assert.Equal("Author č", summary.Author);
    }

    // A message committed with --cleanup=verbatim, or by another tool, may start with blank lines, which Git keeps in %B;
    // Git's subject (%s, git log --oneline) is its first line that is not blank. The rows are %B as Git 2.43 to 2.55 print it.
    [Theory]
    [InlineData("\n\nBody line\n\nmore\n")]
    [InlineData("\r\n\r\nBody line\r\n")]
    [InlineData(" \t\n\nBody line\n")]
    public void WhenTheMessageStartsWithBlankLines_ShouldShowTheFirstLineThatIsNotBlankAsGitDoes(string message) =>
        Assert.Equal("Body line", Record("Author", "author@example.invalid", message).Summary.Subject);

    // Git's subject is the whole first paragraph: each of its lines without the spaces, tabs and carriage return it ends
    // with, joined by one space. The rows are %B and %s as Git 2.43 to 2.55 print them for commits made with --cleanup=verbatim.
    [Theory]
    [InlineData("line one\nline two\n\nbody\n", "line one line two")]
    [InlineData("Subject  \t\nnext line\n", "Subject next line")]
    [InlineData("first\r\nsecond \r\n\r\nbody\r\n", "first second")]
    public void WhenTheFirstParagraphHasSeveralLines_ShouldJoinThemAsGitsSubjectDoes(string message, string subject) =>
        Assert.Equal(subject, Record("Author", "author@example.invalid", message).Summary.Subject);

    // Git's subject keeps the spaces and tabs its first line starts with, after blank lines too.
    [Theory]
    [InlineData("\n  Indented line\n", "  Indented line")]
    [InlineData("\t Lead\n\nbody\n", "\t Lead")]
    public void WhenTheSubjectStartsWithSpacesOrTabs_ShouldKeepThemAsGitDoes(string message, string subject) =>
        Assert.Equal(subject, Record("Author", "author@example.invalid", message).Summary.Subject);

    [Fact]
    public void WhenTheAuthorTheAddressAndTheSubjectAreTooLong_ShouldClipEachWithAnEllipsis()
    {
        var summary = Record(new string('a', 300), new string('b', 300), new string('c', 5000)).Summary;
        Assert.Equal(new string('a', 256) + "…", summary.Author);
        Assert.Equal(new string('b', 256) + "…", summary.Email);
        Assert.Equal(new string('c', 1024) + "…", summary.Subject);
    }

    [Fact]
    public void WhenTheDateIsTooLong_ShouldClipItWithAnEllipsis() =>
        Assert.Equal(new string('9', 64) + "…", CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0\0A\0a@example.invalid\0" + new string('9', 100) + "\0m")).Summary.Date);

    [Fact]
    public void WhenAValueIsExactlyAtTheLimit_ShouldNotClipIt() =>
        Assert.Equal(new string('a', 256), Record(new string('a', 256), "author@example.invalid", "Subject").Summary.Author);

    [Fact]
    public void WhenTheReaderAsksGitForACommit_ShouldAskForTheSixFieldsItExpects()
    {
        Assert.Equal("--format=%H%x00%P%x00%aN%x00%aE%x00%aI%x00%B", CommitRecord.Format);
        Assert.Equal(6, CommitRecord.Fields);
    }

    // The whole record arrives as one span with the field NULs inside it; the message keeps everything after the fifth.
    [Fact]
    public void WhenAWholeRecordIsParsed_ShouldSplitItAtTheHistoryFieldBoundaries()
    {
        var parent = new string('2', 40);
        var record = CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0" + parent + " " + Id + "\0Autor č\0author@example.invalid\02024-01-01T00:00:00+00:00\0Subject\n\nBody with\nnewlines"));
        Assert.Equal([parent, Id], record.Summary.Parents);
        Assert.Equal(Id, record.Summary.Id);
        Assert.Equal("Autor č", record.Summary.Author);
        Assert.Equal("author@example.invalid", record.Summary.Email);
        Assert.Equal("2024-01-01T00:00:00+00:00", record.Summary.Date);
        Assert.Equal("Subject\n\nBody with\nnewlines", record.Message);
    }

    [Fact]
    public void WhenARecordHasNoParent_ShouldHaveNoParents() =>
        Assert.Empty(CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0\0A\0a@example.invalid\0d\0root")).Summary.Parents);

    [Fact]
    public void WhenTheIdIsInvalid_ShouldRejectTheRecord() =>
        Assert.Throws<FormatException>(() => CommitRecord.Parse(Encoding.UTF8.GetBytes("short\0\0A\0a@example.invalid\0d\0m")));

    [Fact]
    public void WhenAParentIsInvalid_ShouldRejectTheRecord() =>
        Assert.Throws<FormatException>(() => CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0zzz\0A\0a@example.invalid\0d\0m")));

    [Fact]
    public void WhenAFieldIsMissing_ShouldRejectTheRecord() =>
        Assert.Throws<FormatException>(() => CommitRecord.Parse(Encoding.UTF8.GetBytes(Id + "\0\0A\0a@example.invalid\0d")));
}
