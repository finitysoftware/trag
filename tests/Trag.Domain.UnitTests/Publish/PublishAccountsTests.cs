using Trag.Domain.Accounts;
using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

public sealed class PublishAccountsTests
{
    [Fact]
    public void WhenAccountsOfBothProvidersExist_ShouldDefaultToGitHub()
    {
        var (gitlab, github, _) = Accounts();
        Assert.Equal(AccountProvider.GitHub, PublishAccounts.DefaultProvider([gitlab, github]));
    }

    [Fact]
    public void WhenOnlyGitLabAccountsExist_ShouldDefaultToGitLab()
    {
        var (gitlab, _, _) = Accounts();
        Assert.Equal(AccountProvider.GitLab, PublishAccounts.DefaultProvider([gitlab]));
    }

    [Fact]
    public void WhenThereAreNoAccounts_ShouldDefaultToGitHub()
    {
        Assert.Equal(AccountProvider.GitHub, PublishAccounts.DefaultProvider([]));
    }

    [Fact]
    public void WhenAProviderIsChosen_ShouldOfferOnlyItsAccounts()
    {
        var (gitlab, github, _) = Accounts();
        Assert.Equal([gitlab], AccountInfo.OfProvider([github, gitlab], AccountProvider.GitLab));
    }

    [Fact]
    public void WhenAProviderHasAnAccount_ShouldSaySoForThatProviderOnly()
    {
        var (gitlab, _, _) = Accounts();
        Assert.True(PublishAccounts.Has([gitlab], AccountProvider.GitLab));
        Assert.False(PublishAccounts.Has([gitlab], AccountProvider.GitHub));
    }

    [Fact]
    public void WhenAccountsAreListed_ShouldOrderThemByProviderThenUserNameIgnoringCase()
    {
        var (gitlab, github, alice) = Accounts();
        Assert.Equal(["alice", "bob", "Zed"], AccountInfo.InDisplayOrder([gitlab, github, alice]).Select(account => account.UserName));
    }

    private static (AccountInfo GitLab, AccountInfo GitHub, AccountInfo Alice) Accounts()
    {
        var gitlab = FakeAccountService.GitLab(user: "Zed");
        var github = FakeAccountService.GitHub(user: "bob");
        var alice = FakeAccountService.GitHub("alice", "alice");
        return (gitlab, github, alice);
    }
}
