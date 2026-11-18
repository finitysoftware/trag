using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Accounts;

public sealed class AccountSettingsTests
{
    [Fact]
    public void WhenAccountsOfBothProvidersAreConnected_ShouldListEachProviderWithOnlyItsOwnInOrder()
    {
        var (github, gitlab, other) = (FakeAccountService.GitHub("one", "alice"), FakeAccountService.GitLab(), FakeAccountService.GitHub("two", "carol"));
        var settings = new AccountSettings([github, gitlab, other], SecretStoreStatus.Available);
        Assert.Equal([github, other], settings.Of(AccountProvider.GitHub));
        Assert.Equal([gitlab], settings.Of(AccountProvider.GitLab));
    }
}
