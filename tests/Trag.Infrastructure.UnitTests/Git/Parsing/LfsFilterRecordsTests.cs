using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// The reads that count Git LFS files, fed what Git 2.55 prints: ls-files --stage -z records, whose paths go on to
// check-attr --stdin -z, and check-attr's answer, the path, the attribute and its value.
public sealed class LfsFilterRecordsTests
{
    private static MemoryStream Output(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task WhenIndexRecordsArrive_ShouldJoinTheirPathsAsCheckAttrTakesThem()
    {
        var paths = await LfsFilterRecords.PathsAsync(Output("100644 0e6acf948e1a7ff5cbce9ca25905ee6f23fc7a4a 0\t.gitattributes\0"
            + "100644 5626abf0f72e58d7a153368ba57db4c673c0e171 0\ta\0100644 587be6b4c3f93f93c489c0111bba5596147a26cb 0\tb\0"), CancellationToken.None);

        Assert.Equal(".gitattributes\0a\0b\0", Encoding.UTF8.GetString(paths));
    }

    // Only the tab ends a record's fields; the path is everything after it.
    [Fact]
    public async Task WhenARecordStartsWithItsTab_ShouldTakeThePathAfterIt() =>
        Assert.Equal("a\0", Encoding.UTF8.GetString(await LfsFilterRecords.PathsAsync(Output("\ta\0"), CancellationToken.None)));

    [Fact]
    public async Task WhenCheckAttrAnswers_ShouldCountOnlyThePathsWhoseFilterIsLfs() =>
        Assert.Equal(1, await LfsFilterRecords.CountLfsAsync(Output("a\0filter\0unspecified\0big.bin\0filter\0lfs\0"), CancellationToken.None));
}
