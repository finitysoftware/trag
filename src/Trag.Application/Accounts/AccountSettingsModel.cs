using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Operations;
using Trag.Application.Ports.Threading;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Accounts;

// The account settings as the Settings window shows them: the accounts by provider, the one whose removal waits for
// confirmation on its own row, the credential store's state, and one sentence for what runs and how it ended. One
// operation runs at a time and Cancel stops it; AccountOperations does the work, and a token stays between the view and
// the service.
public sealed partial class AccountSettingsModel(AccountOperations accounts, IViewDispatcher dispatcher) : ObservableObject
{
    private AccountSettings? settings;

    public Operation Running { get; } = new();
    [ObservableProperty] public partial IReadOnlyList<AccountRow> GitHubAccounts { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountRow> GitLabAccounts { get; private set; } = [];
    [ObservableProperty] public partial string VaultText { get; private set; } = AccountText.CheckingStore;
    // What runs and how the last operation ended. The window says its own lines here too: the GitLab token help when its
    // entry opens, and where to go when the browser cannot open the device page.
    [ObservableProperty] public partial string Status { get; set; } = AccountText.AddAccount;
    [ObservableProperty] public partial AccountInfo? RemovalCandidate { get; private set; }

    public Task<AccountOutcome?> LoadAsync() => RunAsync(accounts.LoadAsync);

    // The GitHub device sign-in: each step it reports is said and handed to progress on the view's thread, only while
    // this sign-in is the one running.
    public Task<AccountOutcome?> SignInGitHubAsync(bool memoryOnly, Action<DeviceLoginProgress> progress) => RunAsync(ct =>
        accounts.SignInGitHubAsync(memoryOnly, ProgressDelivery.Of<DeviceLoginProgress>(value => dispatcher.InvokeAsync(() =>
        {
            if (!Running.Runs(ct)) return;
            Status = AccountText.DeviceLogin(value.State);
            progress(value);
        })), ct));

    // A token entered in the view: the view hands it to the service itself, so only the account that came of it enters here.
    public Task<AccountOutcome?> SignInAsync(Func<CancellationToken, Task<AccountInfo>> signIn) => RunAsync(ct =>
    {
        Status = AccountText.VerifyingGitLab;
        return accounts.SignInAsync(signIn, ct);
    });

    public Task<AccountOutcome?> VerifyAsync(AccountInfo account) => RunAsync(ct =>
    {
        Status = AccountText.CheckingIdentity;
        return accounts.VerifyAsync(account.Id, ct);
    });

    // The account whose row asks to confirm its removal; null ends the question.
    public void RequestRemoval(AccountInfo? account) => RemovalCandidate = account;

    public Task<AccountOutcome?> ConfirmRemovalAsync() =>
        RemovalCandidate is { } account ? RunAsync(ct => accounts.RemoveAsync(account.Id, ct)) : Task.FromResult<AccountOutcome?>(null);

    public void Cancel() => Running.Cancel();

    partial void OnRemovalCandidateChanged(AccountInfo? value) => ShowRows();

    // A refused start answers null; otherwise the outcome, already shown.
    private Task<AccountOutcome?> RunAsync(Func<CancellationToken, Task<AccountOutcome>> work) =>
        Running.RunAsync<AccountOutcome>(async ct => Show(await work(ct)));

    // The settings it read again, a removed candidate, the sentence.
    private AccountOutcome Show(AccountOutcome outcome)
    {
        if (outcome.Settings is { } read)
        {
            settings = read;
            VaultText = AccountText.Vault(read.VaultStatus);
        }
        if (outcome.Removed) RemovalCandidate = null;
        ShowRows();
        Status = outcome.Status;
        return outcome;
    }

    private void ShowRows()
    {
        GitHubAccounts = Rows(AccountProvider.GitHub);
        GitLabAccounts = Rows(AccountProvider.GitLab);
    }

    private AccountRow[] Rows(AccountProvider provider) =>
        settings is null ? [] : [.. settings.Of(provider).Select(account => new AccountRow(account, account.Id == RemovalCandidate?.Id))];
}
