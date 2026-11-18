using Trag.Domain.Catalog;

namespace Trag.Domain.UnitTests.Catalog;

// What a search typed in the repository picker finds: a listed repository by its name or folder, a hosted one by its full
// name, case and the spaces around the search left aside.
public sealed class CatalogSearchTests
{
    private static readonly RepositoryEntry Local = new(Path.Combine("work", "checkout"), "Trag", DateTimeOffset.UnixEpoch);
    private static readonly RemoteRepository Hosted = new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", false, null);

    [Fact]
    public void WhenTheSearchIsPartOfAListedRepositorysName_ShouldFindItWhateverTheCase() => Assert.True(Local.Matches("tRa"));

    [Fact]
    public void WhenTheSearchIsPartOfAListedRepositorysFolder_ShouldFindIt() => Assert.True(Local.Matches("check"));

    [Fact]
    public void WhenTheSearchHasSpacesAround_ShouldFindTheRepositoryWithoutThem() => Assert.True(Local.Matches("  trag  "));

    [Fact]
    public void WhenTheSearchIsEmpty_ShouldFindEveryRepository()
    {
        Assert.True(Local.Matches("   "));
        Assert.True(Hosted.Matches(""));
    }

    [Fact]
    public void WhenTheSearchIsNeitherNameNorFolder_ShouldNotFindTheListedRepository() => Assert.False(Local.Matches("other"));

    [Fact]
    public void WhenTheSearchIsPartOfAHostedRepositorysOwnerOrName_ShouldFindItWhateverTheCase()
    {
        Assert.True(Hosted.Matches("ConToso"));
        Assert.True(Hosted.Matches(" SAND "));
    }

    [Fact]
    public void WhenTheSearchIsNotInTheHostedRepositorysFullName_ShouldNotFindIt() => Assert.False(Hosted.Matches("trag"));
}
