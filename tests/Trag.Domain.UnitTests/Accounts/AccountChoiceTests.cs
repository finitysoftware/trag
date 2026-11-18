using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Accounts;

public sealed class AccountChoiceTests
{
    [Fact]
    public void WhenTheAddressIsOnOneProvidersHost_ShouldOfferGitsOwnCredentialsAndOnlyThatProvidersAccounts()
    {
        var accounts = new[] { FakeAccountService.GitHub(), FakeAccountService.GitLab() };
        Assert.Equal([AccountChoice.External, accounts[0].Id], AccountChoice.ForUrl("https://github.com/alice/trag", accounts).Select(choice => choice.Id));
    }

    [Fact]
    public void WhenSeveralAccountsMatchTheAddress_ShouldOfferGitsOwnCredentialsAndEveryOne()
    {
        var (alice, bob, _) = Team();
        Assert.Equal([AccountChoice.External, "alice", "bob"], AccountChoice.ForUrl("https://github.com/team/trag", [alice, bob]).Select(choice => choice.Id));
    }

    [Fact]
    public void WhenTheChoicesAreShown_ShouldNameGitsOwnCredentialsFirstAndEachAccountByItsUserAndHost()
    {
        var (alice, _, _) = Team();
        Assert.Equal(["Existing Git credentials / SSH agent", "alice · https://github.com"], AccountChoice.ForAccounts([alice]).Select(choice => choice.Display));
    }

    [Theory]
    [InlineData("")]
    [InlineData("git@github.com:alice/trag.git")]
    [InlineData("http://github.com/alice/trag")]
    [InlineData("https://github.com:8443/alice/trag")]
    [InlineData("https://alice@github.com/alice/trag")]
    [InlineData("https://github.com.evil.invalid/alice/trag")]
    public void WhenTheAddressDoesNotMatch_ShouldOfferNoStoredCredentials(string url) => Assert.Empty(AccountChoice.ForUrl(url, [FakeAccountService.GitHub()]));

    [Fact]
    public void WhenOneAccountMatchesAndNothingWasChosen_ShouldStartWithThatAccount()
    {
        var (alice, _, url) = Team();
        Assert.Equal("alice", AccountChoice.Default(AccountChoice.ForUrl(url, [alice]), null)?.Id);
    }

    [Fact]
    public void WhenSeveralAccountsMatchAndNothingWasChosen_ShouldStartWithGitsOwnCredentials()
    {
        var (alice, bob, url) = Team();
        Assert.Equal(AccountChoice.External, AccountChoice.Default(AccountChoice.ForUrl(url, [alice, bob]), null)?.Id);
    }

    [Fact]
    public void WhenTheEarlierChoiceIsOffered_ShouldStartWithIt()
    {
        var (alice, bob, url) = Team();
        Assert.Equal("bob", AccountChoice.Default(AccountChoice.ForUrl(url, [alice, bob]), "bob")?.Id);
    }

    [Fact]
    public void WhenTheEarlierChoiceIsNotOffered_ShouldStartWithTheOnlyAccount()
    {
        var (alice, _, url) = Team();
        Assert.Equal("alice", AccountChoice.Default(AccountChoice.ForUrl(url, [alice]), "bob")?.Id);
    }

    [Fact]
    public void WhenNothingIsOffered_ShouldStartWithNoChoice()
    {
        Assert.Null(AccountChoice.Default([], "alice"));
    }

    [Fact]
    public void WhenGitsOwnCredentialsOrNothingIsChosen_ShouldNameNoTragAccount()
    {
        Assert.Null(AccountChoice.TragAccount(AccountChoice.External)); Assert.Null(AccountChoice.TragAccount(null));
    }

    [Fact]
    public void WhenAnAccountIsChosen_ShouldNameItAsTheTragAccount()
    {
        Assert.Equal("alice", AccountChoice.TragAccount("alice"));
    }

    // Two GitHub accounts, and an address on GitHub that neither user owns.
    private static (AccountInfo Alice, AccountInfo Bob, string Url) Team() =>
        (FakeAccountService.GitHub("alice", "alice"), FakeAccountService.GitHub("bob", "bob"), "https://github.com/team/trag");
}
