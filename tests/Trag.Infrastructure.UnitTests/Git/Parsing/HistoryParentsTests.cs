using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class HistoryParentsTests
{
    [Fact]
    public void WhenACommitIsAMerge_ShouldListBothParentsInOrder()
    {
        var id = new string('a', 40); var first = new string('b', 40); var second = new string('c', 40);
        var merge = new HistoryRecordFilter("", 0).Match(Encoding.UTF8.GetBytes(id + "\0" + first + " " + second + "\0Author\0a@example.invalid\02026-09-14T12:00:00+00:00\0\0Author\0a@example.invalid\0Merge\n\nBody"));
        Assert.Equal([first, second], merge!.Parents); Assert.Equal("Merge", merge.Subject);
    }

    [Fact]
    public void WhenACommitIsARoot_ShouldListNoParentsAndNoNames()
    {
        var id = new string('a', 40);
        var root = new HistoryRecordFilter("", 0).Match(Encoding.UTF8.GetBytes(id + "\0\0Author\0a@example.invalid\02026-09-14T12:00:00+00:00\0\0Author\0a@example.invalid\0Initial"));
        Assert.Empty(root!.Parents); Assert.Empty(root.Refs);
    }

    [Fact]
    public void WhenAParentIdIsMalformed_ShouldRejectTheRecord()
    {
        var id = new string('a', 40);
        Assert.Throws<FormatException>(() => new HistoryRecordFilter("", 0).Match(Encoding.UTF8.GetBytes(id + "\0zzz\0Author\0a@example.invalid\02026-09-14T12:00:00+00:00\0\0Author\0a@example.invalid\0Bad")));
    }
}
