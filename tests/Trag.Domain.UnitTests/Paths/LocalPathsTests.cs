using Trag.Domain.Paths;

namespace Trag.Domain.UnitTests.Paths;

// A folder named two ways is one folder: the key is the full path without a trailing separator, compared the way this
// machine's file system compares names.
public sealed class LocalPathsTests
{
    [Fact]
    public void WhenAFolderIsNamedWithATrailingSeparatorOrRelatively_ShouldHaveOneKey()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trag-key");
        Assert.Equal(LocalPaths.Key(folder), LocalPaths.Key(folder + Path.DirectorySeparatorChar));
        Assert.Equal(LocalPaths.Key(folder), LocalPaths.Key(Path.Combine(folder, "sub", "..")));
    }

    [Fact]
    public void WhenTwoNamesKeyTheSameFolder_ShouldBeTheSameFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trag-key");
        Assert.True(LocalPaths.Same(folder, folder + Path.DirectorySeparatorChar));
        Assert.False(LocalPaths.Same(folder, folder + "-other"));
    }

    // A name Git could never have, with a NUL in it, is keyed as given, without the spaces around it.
    [Fact]
    public void WhenAPathCannotBeMadeFull_ShouldKeyItTrimmed() => Assert.Equal("a\0b", LocalPaths.Key(" a\0b "));
}
