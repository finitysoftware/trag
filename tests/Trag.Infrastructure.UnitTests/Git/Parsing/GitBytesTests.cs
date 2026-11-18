using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// The strict ways of decoding Git's output: bytes outside UTF-8 are refused rather than replaced, and a line keeps all
// but Git's terminator, so a directory name that ends in spaces survives.
public sealed class GitBytesTests
{
    [Fact]
    public void WhenStrictDecodingMeetsBytesOutsideUtf8_ShouldRefuseThem()
    {
        byte[] output = [0xFF, (byte)'\n'];
        Assert.Throws<DecoderFallbackException>(() => GitBytes.StrictText(output));
        Assert.Throws<DecoderFallbackException>(() => GitBytes.StrictLine(output));
    }

    [Fact]
    public void WhenAStrictLineEndsInSpaces_ShouldDropOnlyTheLineBreak() =>
        Assert.Equal("C:/work/dir ", GitBytes.StrictLine("C:/work/dir \n"u8.ToArray()));

    // On Windows a line may also end in CR LF; elsewhere a CR is a character a name can end in.
    [Fact]
    public void WhenAStrictLineEndsInCrLf_ShouldDropTheCrOnlyOnWindows() =>
        Assert.Equal(OperatingSystem.IsWindows() ? "C:/work/dir" : "C:/work/dir\r", GitBytes.StrictLine("C:/work/dir\r\n"u8.ToArray()));
}
