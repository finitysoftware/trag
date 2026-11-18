using Trag.Application.Accounts;
using Trag.Application.Ports.Threading;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Accounts;

// The account settings as the Settings window shows them: one operation at a time that Cancel stops, the sentence for what
// runs and how it ended, the accounts by provider with the one whose removal waits for confirmation, and the store's state.
public sealed class AccountSettingsModelTests
{
    private static readonly AccountInfo Account = FakeAccountService.GitHub();

    private static AccountSettingsModel Model(FakeAccountService service) => new(new AccountOperations(service), new ImmediateViewDispatcher());

    // A second operation is refused while one runs, the removal is not even asked; a cancelled sign-in says so, and the
    // progress it reports afterwards reaches nobody.
    [Fact]
    public async Task WhenASignInRuns_ShouldRefuseEveryOtherOperationAndDropItsProgressOnceCancelled()
    {
        var finished = new TaskCompletionSource<AccountInfo>();
        IProgress<DeviceLoginProgress>? reporter = null;
        var service = new FakeAccountService { Accounts = { Account }, SignInGitHub = (_, progress, _) => { reporter = progress; return finished.Task; } };
        var model = Model(service);
        await model.LoadAsync();
        model.RequestRemoval(Account);
        var heard = new List<DeviceLoginProgress>();

        var signingIn = model.SignInGitHubAsync(false, heard.Add);
        Assert.True(model.Running.IsBusy);
        Assert.Null(await model.ConfirmRemovalAsync());
        var gitLabAsked = false;
        Assert.Null(await model.SignInAsync(_ => { gitLabAsked = true; return Task.FromResult(Account); }));
        Assert.False(gitLabAsked); Assert.Empty(service.Removed);

        model.Cancel();
        finished.SetResult(Account); // the service ignores the cancellation on purpose
        Assert.Equal(AccountText.Canceled, (await signingIn)!.Status);
        reporter!.Report(new(DeviceLoginState.Waiting, "LATE", new("https://github.com/login/device")));
        Assert.Equal(AccountText.Canceled, model.Status); Assert.Empty(heard); Assert.False(model.Running.IsBusy);
    }

    [Fact]
    public async Task WhenTheDeviceSignInReports_ShouldSayItsStateAndHandTheProgressOn()
    {
        var service = new FakeAccountService
        {
            SignInGitHub = async (_, progress, ct) =>
            {
                progress.Report(new(DeviceLoginState.Waiting, "ABCD-1234", new("https://github.com/login/device")));
                await Task.Delay(Timeout.Infinite, ct); return Account;
            }
        };
        var model = Model(service);
        var heard = new List<DeviceLoginProgress>();

        var signingIn = model.SignInGitHubAsync(false, heard.Add);

        Assert.Equal(AccountText.DeviceLogin(DeviceLoginState.Waiting), model.Status);
        Assert.Equal("ABCD-1234", Assert.Single(heard).UserCode);
        model.Cancel(); await signingIn;
    }

    // While the work runs the line says what it checks; then how it ended.
    [Fact]
    public async Task WhenAnAccountIsCheckedOrATokenVerified_ShouldSayWhatRunsThenHowItEnded()
    {
        var verified = new TaskCompletionSource<AccountInfo>();
        var service = new FakeAccountService { Accounts = { Account } };
        var model = Model(service);
        await model.LoadAsync();

        var signingIn = model.SignInAsync(_ => verified.Task);
        Assert.Equal(AccountText.VerifyingGitLab, model.Status);
        verified.SetResult(FakeAccountService.GitLab());
        Assert.True((await signingIn)!.SignedIn);
        Assert.StartsWith("Connected as", model.Status);

        var said = new List<string>();
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AccountSettingsModel.Status)) said.Add(model.Status); };
        await model.VerifyAsync(Account);
        Assert.Equal([AccountText.CheckingIdentity, AccountText.Verified(Account)], said);
    }

    [Fact]
    public async Task WhenTheCredentialStoreIsLocked_ShouldSaySoAndKeepTheQuestionWhenRemovalFails()
    {
        var service = new FakeAccountService
        {
            VaultStatus = SecretStoreStatus.Locked, Accounts = { Account },
            RemoveFailure = new AccountException("Unlock your credential store and retry removal.")
        };
        var model = Model(service);
        await model.LoadAsync();
        Assert.Contains("locked", model.VaultText);

        model.RequestRemoval(Account);
        await model.ConfirmRemovalAsync();

        Assert.True(Assert.Single(model.GitHubAccounts).ConfirmsRemoval); Assert.Contains("Unlock", model.Status);
    }

    // The removal asks on the account's own row and on no other; keeping the account ends the question, confirming removes it.
    [Fact]
    public async Task WhenRemovalIsRequested_ShouldAskOnTheAccountsOwnRowUntilItIsKeptOrRemoved()
    {
        var model = Model(new FakeAccountService { Accounts = { Account, FakeAccountService.GitLab() } });
        await model.LoadAsync();

        model.RequestRemoval(Account);
        Assert.Equal((true, false), (Assert.Single(model.GitHubAccounts).ConfirmsRemoval, Assert.Single(model.GitLabAccounts).ConfirmsRemoval));
        model.RequestRemoval(null);
        Assert.False(Assert.Single(model.GitHubAccounts).ConfirmsRemoval);
        model.RequestRemoval(Account);
        await model.ConfirmRemovalAsync();

        Assert.Empty(model.GitHubAccounts); Assert.False(Assert.Single(model.GitLabAccounts).ConfirmsRemoval);
        Assert.Null(model.RemovalCandidate);
    }
}
