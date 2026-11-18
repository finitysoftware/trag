using Trag.Application.Accounts;
using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Accounts;

// The accounts as their hosts list them: in display order, the owners of one of them, and nothing without a directory.
public sealed class AccountsModelTests
{
    [Fact]
    public async Task WhenTheAccountsAreListed_ShouldComeInDisplayOrder()
    {
        var (accounts, gitlab, bob, alice) = Listed();
        Assert.Equal([alice, bob, gitlab], await accounts.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheOwnersOfAnAccountAreRead_ShouldGiveThatAccountsOwn()
    {
        var (accounts, _, bob, alice) = Listed();
        Assert.Equal(["team"], (await accounts.OwnersAsync(bob, CancellationToken.None)).Select(owner => owner.Id));
        Assert.Empty(await accounts.OwnersAsync(alice, CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheAccountsCanBeRead_ShouldGiveThemToTheCallerInDisplayOrder()
    {
        var directory = new FakeRepositoryCatalog();
        directory.Accounts.AddRange([FakeAccountService.GitLab(), FakeAccountService.GitHub()]);
        var accounts = new AccountsModel(directory);
        Assert.Equal([AccountProvider.GitHub, AccountProvider.GitLab], (await accounts.TryListAsync(CancellationToken.None))!.Select(a => a.Provider));
    }

    // The caller says so and goes on.
    [Fact]
    public async Task WhenTheAccountsCannotBeRead_ShouldBeNoAccountsToTheCaller()
    {
        var directory = new FakeRepositoryCatalog();
        directory.Accounts.AddRange([FakeAccountService.GitLab(), FakeAccountService.GitHub()]);
        var accounts = new AccountsModel(directory);
        directory.AccountsError = new IOException("offline");
        Assert.Null(await accounts.TryListAsync(CancellationToken.None));
    }

    // A GitLab account and two GitHub ones out of display order; only bob owns an organization.
    private static (AccountsModel Accounts, AccountInfo GitLab, AccountInfo Bob, AccountInfo Alice) Listed()
    {
        var catalog = new FakeRepositoryCatalog();
        var gitlab = FakeAccountService.GitLab("lab", "carol");
        var bob = FakeAccountService.GitHub("bob", "bob");
        var alice = FakeAccountService.GitHub("alice", "Alice");
        catalog.Accounts.AddRange([gitlab, bob, alice]);
        catalog.Owners[bob.Id] = [new("team", "The team")];
        return (new AccountsModel(catalog), gitlab, bob, alice);
    }
}
