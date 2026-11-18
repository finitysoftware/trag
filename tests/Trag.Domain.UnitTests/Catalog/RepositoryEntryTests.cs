using Trag.Domain.Catalog;

namespace Trag.Domain.UnitTests.Catalog;

public sealed class RepositoryEntryTests
{
    [Fact]
    public void WhenAPathHasAFolder_ShouldNameTheEntryAfterItsLastFolderWithOrWithoutASeparator()
    {
        var root = Path.GetFullPath("catalog-root");
        var separator = Path.DirectorySeparatorChar;
        Assert.Equal("repo", RepositoryEntry.DisplayName(Path.Combine(root, "repo") + separator));
        Assert.Equal("repo", RepositoryEntry.DisplayName(Path.Combine(root, "repo")));
    }

    [Fact]
    public void WhenAPathHasNoFolder_ShouldNameTheEntryAsThePathItself()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root, RepositoryEntry.DisplayName(root));
    }
}
