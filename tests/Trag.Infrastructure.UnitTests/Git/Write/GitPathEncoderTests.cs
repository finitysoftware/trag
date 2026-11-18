using System.Text;
using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Write;

namespace Trag.Infrastructure.UnitTests.Git.Write;

public sealed class GitPathEncoderTests
{
    [Fact]
    public void WhenTheSelectionRepeatsAPath_ShouldWriteEveryPathOnceAsRawBytesEndingInNul()
    {
        var encoded = GitPathEncoder.Encode([Path("one.txt"), Path("dir/two č.txt"), Path("one.txt")]);
        Assert.Equal(Encoding.UTF8.GetBytes("one.txt\0dir/two č.txt\0"), encoded);
    }

    [Fact]
    public void WhenANameIsNotUtf8_ShouldKeepItsBytes()
    {
        var raw = new GitPath([0xFF, 0xFE, (byte)'x']);
        Assert.Equal(new byte[] { 0xFF, 0xFE, (byte)'x', 0 }, GitPathEncoder.Encode([raw, raw]));
    }

    [Fact]
    public void WhenTheSelectionIsEmpty_ShouldEncodeToNothing() => Assert.Empty(GitPathEncoder.Encode([]));

    private static GitPath Path(string value) => new(Encoding.UTF8.GetBytes(value));
}
