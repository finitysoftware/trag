using Trag.Application.Accounts;
using Trag.Application.Ports.Threading;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.TestSupport;
using Trag.ViewModels.Accounts;

namespace Trag.ViewModels.UnitTests.Accounts;

// The settings' accounts page over the settings model: the commands wait for the operation that runs, the device code
// shows and its page opens once, a token entry opens on the account's own server, and the window's browser is asked.
public sealed class AccountsViewModelTests
{
    private static readonly AccountInfo Account = FakeAccountService.GitHub();

    private static AccountsViewModel Model(FakeAccountService service) =>
        new(new AccountSettingsModel(new AccountOperations(service), new ImmediateViewDispatcher()));

    [Fact]
    public async Task WhenASignInRuns_ShouldHoldTheOtherAccountCommandsAndIgnoreItsLateProgressAfterClosing()
    {
        var finished = new TaskCompletionSource<AccountInfo>();
        IProgress<DeviceLoginProgress>? progress = null;
        var service = new FakeAccountService
        {
            SignInGitHub = (_, reporter, _) => { progress = reporter; return finished.Task; }
        };
        service.Accounts.Add(Account);
        var model = Model(service);
        await model.LoadAsync();
        model.RequestRemovalCommand.Execute(Account);
        var login = model.AddGitHubCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        Assert.False(model.VerifyAccountCommand.CanExecute(Account));
        Assert.False(model.ConfirmRemovalCommand.CanExecute(null));

        model.Close();
        finished.SetResult(Account); // This service deliberately ignores cancellation.
        await login;
        Assert.Contains("canceled", model.Status);
        var status = model.Status;
        progress!.Report(new(DeviceLoginState.Waiting, "LATE", new("https://github.com/login/device")));
        Assert.Equal(status, model.Status);
        Assert.False(model.Login.HasCode);
        Assert.True(model.AddGitHubCommand.CanExecute(null));
    }

    private static FakeAccountService Service() => new()
    {
        VaultStatus = SecretStoreStatus.Locked,
        Accounts = { Account },
        RemoveFailure = new AccountException("Unlock your credential store and retry removal."),
        SignInGitHub = async (_, progress, ct) =>
        {
            progress.Report(new(DeviceLoginState.Waiting, "ABCD-1234", new("https://github.com/login/device")));
            progress.Report(new(DeviceLoginState.SlowedDown, "ABCD-1234", new("https://github.com/login/device")));
            await Task.Delay(Timeout.Infinite, ct); return Account;
        }
    };

    [Fact]
    public async Task WhenTheDeviceSignInStarts_ShouldShowTheCodeOpenTheBrowserOnceAndClearTheCodeOnCancel()
    {
        var model = Model(Service());
        await model.LoadAsync(); var pages = new List<Uri>(); model.OpenBrowser = page => { pages.Add(page); return true; };
        var pending = model.AddGitHubCommand.ExecuteAsync(null);
        Assert.True(model.Login.HasCode); Assert.Equal("ABCD-1234", model.Login.UserCode); Assert.True(model.IsBusy);
        Assert.Single(pages); Assert.Equal("https://github.com/login/device", pages[0].AbsoluteUri);
        model.CancelCommand.Execute(null); await pending;
        Assert.False(model.IsBusy); Assert.False(model.Login.HasCode); Assert.Equal("", model.Login.UserCode); Assert.Contains("canceled", model.Status);
    }

    // The rows and the store's state are the settings model's; the page shows them as they change.
    [Fact]
    public async Task WhenTheAccountsLoadAndARemovalIsAsked_ShouldShowTheRowsAndTheStoreAsTheModelHasThem()
    {
        var model = Model(Service());
        await model.LoadAsync();
        Assert.Contains("locked", model.VaultText);
        model.RequestRemovalCommand.Execute(Account);
        Assert.True(Assert.Single(model.GitHubAccounts).ConfirmsRemoval);
        model.KeepAccountCommand.Execute(null);
        Assert.False(Assert.Single(model.GitHubAccounts).ConfirmsRemoval);
    }

    [Fact]
    public async Task WhenATokenIsReplaced_ShouldOpenTheEntryOnTheAccountsServerWithItsStorageChoice()
    {
        var service = Service();
        var sessionOnly = FakeAccountService.GitLab(host: "https://git.example.invalid:8443/team/") with { IsPersistent = false };
        service.Accounts.Add(sessionOnly);
        var model = Model(service);
        await model.LoadAsync();
        model.ReplaceTokenCommand.Execute(sessionOnly);
        Assert.True(model.ShowGitLabEntry); Assert.Equal("https://git.example.invalid:8443/team/", model.GitLabAddress); Assert.True(model.MemoryOnly);
        model.CancelCommand.Execute(null);
        model.ReplaceTokenCommand.Execute(FakeAccountService.GitLab());
        Assert.True(model.ShowGitLabEntry); Assert.Equal("https://gitlab.com/", model.GitLabAddress); Assert.False(model.MemoryOnly);
    }

    // A token that connected an account closes its entry; one the host refused keeps it open with the reason.
    [Fact]
    public async Task WhenAGitLabTokenIsEntered_ShouldCloseTheEntryOnlyWhenItConnectedAnAccount()
    {
        var model = Model(new FakeAccountService());
        model.AddGitLabCommand.Execute(null);

        await model.RunGitLabEntryAsync(_ => throw new AccountException("The token was refused."));
        Assert.True(model.ShowGitLabEntry); Assert.Equal("The token was refused.", model.Status);
        await model.RunGitLabEntryAsync(_ => Task.FromResult(FakeAccountService.GitLab()));

        Assert.False(model.ShowGitLabEntry);
    }

    [Fact]
    public async Task WhenTheBrowserCannotOpenTheDevicePage_ShouldSayInTheStatusWhereToGo()
    {
        var model = Model(Service());
        model.OpenBrowser = _ => false;
        await model.LoadAsync();
        model.OpenDevicePageCommand.Execute(null);
        Assert.Equal("Open https://github.com/login/device in your browser and enter the displayed code.", model.Status);
    }
}
