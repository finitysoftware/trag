using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// One reader owns the record boundary rule for every -z output Trag reads whole: it yields lazily, and it reaches
// the terminator check only when fully enumerated.
public sealed class NulRecordReaderTests
{
    private static string[] Text(IEnumerable<byte[]> records) => records.Select(Encoding.UTF8.GetString).ToArray();

    [Fact]
    public void WhenTheTerminatorIsMissing_ShouldRejectTheOutputOnceItIsFullyEnumerated()
    {
        var records = NulRecordReader.Records(Encoding.UTF8.GetBytes("abc\0subject"));
        var error = Assert.Throws<FormatException>(() => Text(records));
        Assert.Equal("Missing NUL terminator.", error.Message);
    }

    [Fact]
    public void WhenTheTerminatorIsMissing_ShouldStillHandOutTheCompleteRecordsBeforeTheCut() =>
        Assert.Equal("abc", Encoding.UTF8.GetString(NulRecordReader.Records(Encoding.UTF8.GetBytes("abc\0subject")).First()));

    [Fact]
    public void WhenTerminatorsFollowEachOther_ShouldReadAnEmptyRecordBetweenThem() =>
        Assert.Equal(["abc", "", "mail"], Text(NulRecordReader.Records(Encoding.UTF8.GetBytes("abc\0\0mail\0"))));

    [Fact]
    public void WhenTheOutputStartsWithATerminator_ShouldReadAnEmptyFirstRecord() =>
        Assert.Equal(["", "abc"], Text(NulRecordReader.Records(Encoding.UTF8.GetBytes("\0abc\0"))));

    [Fact]
    public void WhenTheOutputIsEmpty_ShouldHaveNoRecords() => Assert.Empty(NulRecordReader.Records([]));

    // Git's -z output is why paths and subjects survive intact.
    [Fact]
    public void WhenARecordHasTabsAndNewlines_ShouldKeepThemAsContent() =>
        Assert.Equal(["new\tname\nfile.cs"], Text(NulRecordReader.Records(Encoding.UTF8.GetBytes("new\tname\nfile.cs\0"))));

    [Fact]
    public void WhenARecordIsNotUtf8_ShouldKeepItsBytes() => Assert.Equal([new byte[] { 0xff, 0xfe }], NulRecordReader.Records([0xff, 0xfe, 0]));
}
