using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class HistoryRecordFilterTests
{
    // Each is in the record below, in another letter case or as it is: the ID, the author with a letter outside ASCII,
    // the address in part and as the whole field, the author and address .mailmap gives, the body, and text that would
    // mean something to Git or to a regular expression.
    private static readonly string[] Queries = ["AAAAAA", "AUTOR Č", "AUTHOR@", "AUTHOR@EXAMPLE.INVALID", "MAPPED Đ", "MAPPED@", "body ž", "--all [a-z].*"];

    private static byte[] Record() => Encoding.UTF8.GetBytes(new string('a', 40)
        + "\0\0Autor č\0author@example.invalid\02026-09-14\0\0Mapped đ\0mapped@example.invalid\0Subject\n\nBody Ž test --all [a-z].*");

    [Fact]
    public void WhenTheQueryIsInAnySearchedFieldAndAMatchIsLeftToSkip_ShouldSkipItAndCountItOff()
    {
        foreach (var query in Queries)
        {
            var filter = new HistoryRecordFilter(query, 1);
            Assert.Null(filter.Match(Record()));
            Assert.Equal(0, filter.MatchesToSkip);
        }
    }

    [Fact]
    public void WhenTheQueryIsInAnySearchedFieldAndTheMatchesToSkipAreUsedUp_ShouldReturnTheCommit()
    {
        foreach (var query in Queries)
        {
            var filter = new HistoryRecordFilter(query, 1);
            filter.Match(Record());
            Assert.Equal("Subject", filter.Match(Record())!.Subject);
        }
    }

    // The row shows the author as .mailmap names them; the author as written is only searched.
    [Fact]
    public void WhenACommitMatches_ShouldNameItsAuthorAsMailmapNamesThem()
    {
        var commit = new HistoryRecordFilter("", 0).Match(Record())!;
        Assert.Equal(("Mapped đ", "mapped@example.invalid"), (commit.Author, commit.Email));
    }

    // The row's date is the author date from its own field, not the names Git keeps on the commit beside it.
    [Fact]
    public void WhenACommitMatches_ShouldGiveItsAuthorDate() => Assert.Equal("2026-09-14", new HistoryRecordFilter("", 0).Match(Record())!.Date);

    // A message Git wrote in another encoding without saying so still holds the ASCII a search asks for, and a non-ASCII
    // query is looked for in a message too long for the stack.
    [Fact]
    public void WhenTheMessageIsNotValidUtf8OrLongerThanOneKilobyte_ShouldStillMatchAndNotThrow()
    {
        var head = Encoding.UTF8.GetBytes(new string('a', 40) + "\0\0Author\0author@example.invalid\02026-09-14\0\0Author\0author@example.invalid\0");
        byte[] invalid = [.. head, .. "Subject "u8, 0xC3, 0x28, 0xFF, .. " needle"u8];
        byte[] longer = [.. head, .. Encoding.UTF8.GetBytes("Subject\n\n" + new string('x', 4000) + " nađi")];

        Assert.NotNull(new HistoryRecordFilter("NEEDLE", 0).Match(invalid));
        Assert.NotNull(new HistoryRecordFilter("NAĐI", 0).Match(longer));
        Assert.Null(new HistoryRecordFilter("žžž", 0).Match(longer));
    }

    [Fact]
    public void WhenTheQueryIsInNoField_ShouldMatchNothingAndSkipNothing()
    {
        var unmatched = new HistoryRecordFilter("not present", 2);
        Assert.Null(unmatched.Match(Record()));
        Assert.Equal(2, unmatched.MatchesToSkip);
    }

    // The names Git keeps on the commit come in their own field, between the date and the message.
    [Fact]
    public void WhenACommitCarriesNames_ShouldReturnThemWithIt()
    {
        var record = Encoding.UTF8.GetBytes(new string('a', 40) + "\0\0Author\0a@example.invalid\02026-09-14\0HEAD -> refs/heads/main, refs/remotes/origin/main\0Author\0a@example.invalid\0Subject");
        var commit = new HistoryRecordFilter("", 0).Match(record)!;
        Assert.Equal(["main", "origin/main"], commit.Refs.Select(name => name.Name));
        Assert.Equal("Subject", commit.Subject);
    }

    [Fact]
    public void WhenCommitsAreRejected_ShouldAllocateNothingPerCommit()
    {
        var bytes = Record();
        // One filter per page, never one per record: that is what keeps the scan free of per-commit allocations.
        var filter = new HistoryRecordFilter("never matches", 0);
        filter.Match(bytes);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) filter.Match(bytes);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 4096);
    }
}
