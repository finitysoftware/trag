using Trag.Domain.Catalog;

namespace Trag.Domain.UnitTests.Catalog;

public sealed class CloneTargetsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "trag-clone-targets");
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "trag-home");

    [Fact]
    public void WhenTheFolderNameIsBlank_ShouldNameItRepository()
    {
        Assert.Equal("repository", CloneTargets.Safe("  "));
    }

    [Fact]
    public void WhenTheFolderNameHasAnInvalidCharacter_ShouldReplaceItWithADash()
    {
        Assert.Equal("a-b", CloneTargets.Safe("a" + Path.GetInvalidFileNameChars()[0] + "b"));
    }

    [Fact]
    public void WhenTheFolderNameHasSpacesAround_ShouldTrimThem()
    {
        Assert.Equal("plain", CloneTargets.Safe(" plain "));
    }

    // The recent repository sits under a folder named after its owner: the new owner gets a sibling folder.
    [Fact]
    public void WhenTheMostRecentRepositoryHasAnOrigin_ShouldSuggestTheNewOwnersFolderBesideItsOwners()
    {
        var recent = Recent();
        Assert.Equal(Path.Combine(Root, "contoso", "sandbox"), CloneTargets.Suggest("https://github.com/contoso/sandbox.git", "fallback", "fallback", [recent], Home));
    }

    // Without an origin the parent itself is the root.
    [Fact]
    public void WhenTheMostRecentRepositoryHasNoOrigin_ShouldSuggestTheOwnersFolderInItsParent()
    {
        var flat = Recent() with { RemoteUrl = null };
        Assert.Equal(Path.Combine(Root, "acme", "contoso", "sandbox"), CloneTargets.Suggest("https://github.com/contoso/sandbox.git", "x", "y", [flat], Home));
    }

    [Fact]
    public void WhenTheAddressHasNoOwnerAndName_ShouldFallBackToTheAccountAndTheRepositoryName()
    {
        var flat = Recent() with { RemoteUrl = null };
        Assert.Equal(Path.Combine(Root, "acme", "alice", "tool"), CloneTargets.Suggest("not a url", "alice", "tool", [flat], Home));
    }

    [Fact]
    public void WhenNoRepositoryIsListed_ShouldSuggestAFolderUnderReposInTheHomeFolder()
    {
        Assert.Equal(Path.Combine(Home, "Repos", "alice", "tool"), CloneTargets.Suggest("x", "alice", "tool", [], Home));
    }

    [Fact]
    public void WhenTheUserPicksAFolder_ShouldCloneIntoItUnderTheRepositoryName()
    {
        Assert.Equal(Path.Combine(Root, "picked", "sandbox"), CloneTargets.Under(Path.Combine(Root, "picked"), "https://github.com/contoso/sandbox.git", "fallback"));
    }

    // Widgets of acme, cloned under a folder named after its owner.
    private static RepositoryEntry Recent()
    {
        var now = DateTimeOffset.UtcNow;
        return new RepositoryEntry(Path.Combine(Root, "acme", "widgets"), "widgets", now, now, "https://github.com/acme/widgets.git");
    }
}
