using Trag.Domain.Accounts;

namespace Trag.Domain.UnitTests.Accounts;

// AccountEndpoints.Matches decides whether a Trag account's token may be offered for a Git endpoint; every clause is a security rule.
public sealed class AccountEndpointsTests
{
    private static AccountInfo Account(string origin) =>
        new("id", AccountProvider.GitHub, "1", "alice", new("https://api.example.invalid/"), new(origin), true, DateTimeOffset.UtcNow, null);

    [Theory]
    [InlineData("https://github.com/", "https://github.com/acme/repo.git", true)]
    [InlineData("https://github.com/", "https://GitHub.COM/acme/repo.git", true)]
    [InlineData("https://github.com/", "https://github.com/", true)]
    [InlineData("https://github.com/", "http://github.com/acme/repo.git", false)]
    [InlineData("https://github.com/", "https://user@github.com/acme/repo.git", false)]
    [InlineData("https://github.com/", "https://user:secret@github.com/acme/repo.git", false)]
    [InlineData("https://github.com/", "https://github.com/acme/repo.git?token=1", false)]
    [InlineData("https://github.com/", "https://github.com/acme/repo.git#fragment", false)]
    [InlineData("https://github.com/", "https://github.com.evil.invalid/acme/repo.git", false)]
    [InlineData("https://gitlab.example:8443/", "https://gitlab.example/group/repo.git", false)]
    [InlineData("https://gitlab.example:8443/", "https://gitlab.example:8443/group/repo.git", true)]
    [InlineData("https://gitlab.example/", "https://gitlab.example:443/group/repo.git", true)]
    [InlineData("https://host/base/", "https://host/base/repo.git", true)]
    [InlineData("https://host/base/", "https://host/base", true)]
    [InlineData("https://host/base/", "https://host/basement/repo.git", false)]
    [InlineData("https://host/base/", "https://host/other/repo.git", false)]
    [InlineData("https://host/base/", "https://host/", false)]
    public void WhenAnEndpointIsCheckedForAnAccount_ShouldMatchOnlyHttpsUnderItsOriginWithoutUserInfoQueryOrFragment(string origin, string endpoint, bool expected) =>
        Assert.Equal(expected, AccountEndpoints.Matches(new(endpoint), Account(origin)));

    [Theory]
    [InlineData("https://gitlab.example:8443/team/", true)]
    [InlineData("http://gitlab.example/", false)]
    [InlineData("https://user@gitlab.example/", false)]
    [InlineData("https://gitlab.example/?token=1", false)]
    [InlineData("https://gitlab.example/#x", false)]
    public void WhenAnAddressIsChecked_ShouldBeCleanOnlyAsHttpsWithNothingButHostPortAndPath(string address, bool expected) =>
        Assert.Equal(expected, AccountEndpoints.IsClean(new(address)));

    [Fact]
    public void WhenTheAddressIsRelative_ShouldNotBeClean() => Assert.False(AccountEndpoints.IsClean(new("acme/repo.git", UriKind.Relative)));

    [Fact]
    public void WhenTheEndpointIsRelative_ShouldNeverMatch() =>
        Assert.False(AccountEndpoints.Matches(new("acme/repo.git", UriKind.Relative), Account("https://github.com/")));
}
