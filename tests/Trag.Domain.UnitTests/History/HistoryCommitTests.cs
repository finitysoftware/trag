using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.History;

public sealed class HistoryCommitTests
{
    [Fact]
    public void WhenACommitIsNamedInAMenu_ShouldShowItsShortIdAndSubject() =>
        Assert.Equal("cccccccccc · Fix it", new HistoryCommit(new string('c', 40), "Ann", "ann@example.invalid", "2026-10-10", "Fix it").Caption);

    [Fact]
    public void WhenTheAuthorDateIsIso_ShouldShowItsLocalDateAndTime()
    {
        var commit = Commit();
        var expected = DateTimeOffset.Parse("2026-09-14T18:43:12+02:00", System.Globalization.CultureInfo.InvariantCulture).ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, commit.DateText);
        Assert.Equal(16, commit.DateText.Length);
    }

    [Fact]
    public void WhenTheAuthorDateIsNoDate_ShouldShowItAsItIs()
    {
        var commit = Commit();
        Assert.Equal("not a date", (commit with { Date = "not a date" }).DateText);
    }

    // In a file's history the commit that renamed or copied the file says what it came from; the others say nothing.
    [Fact]
    public void WhenTheFileCameFromAnotherPath_ShouldSaySo()
    {
        var commit = Commit() with { Path = Snapshots.Path("src/new.txt") };

        Assert.Equal(("", "from src/old.txt"), (commit.OriginalPathText, (commit with { OriginalPath = Snapshots.Path("src/old.txt") }).OriginalPathText));
    }

    private static HistoryCommit Commit() =>
        new(new string('a', 40), "Author", "author@example.invalid", "2026-09-14T18:43:12+02:00", "Subject");
}
