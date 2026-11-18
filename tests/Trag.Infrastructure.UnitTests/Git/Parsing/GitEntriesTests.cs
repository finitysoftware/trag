using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// check-attr -z --stdin filter as Git 2.55 answers it: for each path the path, the attribute and its value, so only every
// third field says whether Git LFS filters a path.
public sealed class GitEntriesTests
{
    private static byte[][] Fields(string answer) => [.. NulRecordReader.Records(Encoding.UTF8.GetBytes(answer))];

    [Theory]
    [InlineData("lfs\0filter\0unspecified\0big.bin\0filter\0lfs\0", true)]
    [InlineData("lfs\0filter\0unspecified\0", false)]
    public void WhenCheckAttrAnswers_ShouldFindTheLfsFilterOnlyInAValue(string answer, bool lfs) =>
        Assert.Equal(lfs, GitEntries.HaveLfsFilter(Fields(answer)));
}
