using Trag.Application.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Accounts;

public sealed class AccountOperationsTests
{
    [Fact]
    public async Task WhenTheAccountsLoad_ShouldSaySoAndListThem()
    {
        var accounts = WithOneAccount();
        var loaded = await accounts.LoadAsync(CancellationToken.None);
        Assert.Equal(AccountText.Loaded, loaded.Status); Assert.Single(loaded.Settings!.Accounts);
    }

    [Fact]
    public async Task WhenTheUserSignsIn_ShouldSayWhoIsConnected()
    {
        var accounts = WithOneAccount();
        var signedIn = await accounts.SignInAsync(_ => Task.FromResult(FakeAccountService.GitHub()), CancellationToken.None);
        Assert.True(signedIn.SignedIn); Assert.StartsWith("Connected as alice", signedIn.Status);
    }

    [Fact]
    public async Task WhenAnAccountIsRemoved_ShouldSaySo()
    {
        var accounts = WithOneAccount();
        var removed = await accounts.RemoveAsync("account-1", CancellationToken.None);
        Assert.True(removed.Removed); Assert.Equal(AccountText.Removed, removed.Status);
    }

    [Fact]
    public async Task WhenTheRemovalFailsWithAnAccountError_ShouldRemoveNothingAndSayTheErrorsSentence()
    {
        var service = new FakeAccountService { RemoveFailure = new AccountException("Unlock your credential store and retry removal.") };
        var accounts = new AccountOperations(service);
        var refused = await accounts.RemoveAsync("account-1", CancellationToken.None);
        Assert.False(refused.Succeeded); Assert.False(refused.Removed); Assert.StartsWith("Unlock", refused.Status);
    }

    [Fact]
    public async Task WhenTheSignInFails_ShouldSayItFailed()
    {
        var accounts = new AccountOperations(new FakeAccountService());
        Assert.Equal(AccountText.Failed, (await accounts.SignInAsync(_ => throw new IOException("offline"), CancellationToken.None)).Status);
    }

    // The sign-in ignores the token; the operation still ends as cancelled.
    [Fact]
    public async Task WhenTheSignInIsCanceled_ShouldSaySo()
    {
        var accounts = new AccountOperations(new FakeAccountService());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Equal(AccountText.Canceled, (await accounts.SignInAsync(_ => Task.FromResult(FakeAccountService.GitHub()), cancel.Token)).Status);
    }

    private static AccountOperations WithOneAccount() =>
        new(new FakeAccountService { Accounts = { FakeAccountService.GitHub() } });
}
