using Trag.Domain.Paths;

namespace Trag.Domain.UnitTests.Paths;

public sealed class GitPathTests
{
    [Fact]
    public void WhenAPathHoldsTabsNewlinesOrBackslashes_ShouldEscapeThemSoItStaysOnOneRow()
    {
        var path = new GitPath("a\tb\nc\\d\re"u8.ToArray());
        Assert.Equal("a\tb\nc\\d\re", path.Display);
        Assert.Equal("a\\tb\\nc\\\\d\\re", path.DisplayEscaped);
        Assert.True(path.IsDisplayable);
    }

    // Any other control character, a line or paragraph separator and a mark that turns the text's direction would garble
    // the row or disguise the name, as in the discard question; a joiner that a script needs stays as it is.
    [Theory]
    [InlineData("a\u0007b", "a\\u0007b")]
    [InlineData("a\u007Fb", "a\\u007Fb")]
    [InlineData("a\u2028b", "a\\u2028b")]
    [InlineData("invoice\u202Efdp.exe", "invoice\\u202Efdp.exe")]
    [InlineData("a\u2066b\u2069", "a\\u2066b\\u2069")]
    [InlineData("\u0645\u06cc\u200c\u062e\u0648\u0627\u0647\u0645", "\u0645\u06cc\u200c\u062e\u0648\u0627\u0647\u0645")]
    public void WhenAPathHoldsOtherControlOrDirectionCharacters_ShouldEscapeThemAndKeepJoiners(string name, string shown) =>
        Assert.Equal(shown, new GitPath(System.Text.Encoding.UTF8.GetBytes(name)).DisplayEscaped);

    [Fact]
    public void WhenAPathIsNotUtf8_ShouldBeShownButNotDisplayable()
    {
        var path = new GitPath([0x61, 0xFF, 0x62]);
        Assert.Equal("a�b", path.Display);
        Assert.False(path.IsDisplayable);
    }

    [Fact]
    public void WhenTwoReadsHaveTheSameBytes_ShouldBeTheSamePathWithTheSameKey()
    {
        var first = new GitPath("src/a.cs"u8.ToArray());
        var second = new GitPath("src/a.cs"u8.ToArray());
        Assert.NotEqual(first, second); // a record compares the array by reference
        Assert.True(first.IsSame(second)); Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void WhenTwoPathsHaveDifferentBytes_ShouldNotBeTheSameNorShareAKey()
    {
        var first = new GitPath("src/a.cs"u8.ToArray());
        Assert.False(first.IsSame(new GitPath("src/b.cs"u8.ToArray())));
        Assert.NotEqual(first.Key, new GitPath([0xFF]).Key);
    }

    [Fact]
    public void WhenThereIsNoOtherPath_ShouldNotBeTheSame()
    {
        var first = new GitPath("src/a.cs"u8.ToArray());
        Assert.False(first.IsSame(null));
    }

    // A rename's original path, absent on both sides, is the same; absent on one side only, it is not.
    [Fact]
    public void WhenEitherPathMayBeAbsent_ShouldBeTheSameWhenBothAreAbsentOrHaveTheSameBytes()
    {
        var first = new GitPath("src/a.cs"u8.ToArray());
        Assert.True(GitPath.Same(null, null)); Assert.True(GitPath.Same(first, new GitPath("src/a.cs"u8.ToArray())));
        Assert.False(GitPath.Same(first, null)); Assert.False(GitPath.Same(null, first));
    }
}
