using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

public sealed class RemoteUrlsTests
{
    [Theory]
    [InlineData("https://github.com/alice/trag", true)]
    [InlineData("git@github.com:alice/trag.git", true)]
    [InlineData("D:\\repos\\trag", true)]
    [InlineData(" ../trag.git ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("-option", false)]
    [InlineData("two addresses", false)]
    [InlineData("url\nother", false)]
    public void WhenAnAddressIsChecked_ShouldAcceptGitAddressesAndRejectInvalidArguments(string? address, bool valid) =>
        Assert.Equal(valid, RemoteUrls.IsValidAddress(address));

    // A password goes and its user name stays; a token given as the user name goes whole; an address without either, a
    // user name alone included, is left as it is.
    [Theory]
    [InlineData("https://acme:s%40cret@github.com/acme/trag.git", "https://acme@github.com/acme/trag.git")]
    [InlineData(" https://ghp_token@github.com/a@b/trag ", "https://github.com/a@b/trag")]
    [InlineData("https://:pat@dev.azure.com/acme/_git/trag", "https://dev.azure.com/acme/_git/trag")]
    [InlineData("https://acme@github.com/acme/trag.git", "https://acme@github.com/acme/trag.git")]
    [InlineData("git@github.com:acme/trag.git", "git@github.com:acme/trag.git")]
    public void WhenAnAddressIsShownForEditing_ShouldLeaveOutItsPasswordOrToken(string url, string shown) =>
        Assert.Equal(shown, RemoteUrls.WithoutCredentials(url));

    [Fact]
    public void WhenTheTrimmedAddressIsAtTheLengthLimit_ShouldBeValid() => Assert.True(RemoteUrls.IsValidAddress(" " + new string('a', 2048) + " "));

    [Fact]
    public void WhenTheAddressIsOverTheLengthLimit_ShouldBeInvalid() => Assert.False(RemoteUrls.IsValidAddress(new string('a', 2049)));

    [Theory]
    [InlineData("https://github.com/Owner/Repo.git", "github.com/owner/repo")]
    [InlineData("git@github.com:Owner/Repo.git", "github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo/", "github.com/owner/repo")]
    [InlineData("https://host:8443/a/b.GIT", "host:8443/a/b")]
    [InlineData("https://host:443/a/b", "host/a/b")]
    [InlineData("  https://host/a/b/  ", "host/a/b")]
    [InlineData("C:\\Repos\\Trag", "c:/repos/trag")]
    [InlineData("host:owner/repo", "host:owner/repo")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void WhenAnAddressIsNormalized_ShouldGiveEquivalentAddressesTheSameForm(string? url, string expected) => Assert.Equal(expected, RemoteUrls.Normalize(url));

    [Fact]
    public void WhenTheSameRepositoryIsGivenInScpAndHttpsForm_ShouldNormalizeBothAlike() =>
        Assert.Equal(RemoteUrls.Normalize("git@github.com:Acme/Alpha.git"), RemoteUrls.Normalize("https://github.com/acme/alpha.git"));

    [Theory]
    [InlineData("https://github.com/acme/repo.git", "acme", "repo")]
    [InlineData("https://host/group/sub/repo", "sub", "repo")]
    [InlineData("https://host/repo", null, "repo")]
    [InlineData("repo", null, null)]
    [InlineData("", null, null)]
    public void WhenAnAddressIsReadForItsOwnerAndName_ShouldNeedThreeSegmentsForTheOwnerAndTwoForTheName(string url, string? owner, string? name)
    {
        Assert.Equal(owner, RemoteUrls.Owner(url));
        Assert.Equal(name, RemoteUrls.Name(url));
    }
}
