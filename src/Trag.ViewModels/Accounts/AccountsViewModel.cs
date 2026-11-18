using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Accounts;

// The account settings: the connected accounts, the GitHub device sign-in and the GitLab token entry, verification and
// removal. The settings model runs them one at a time and says what runs and how it ended; this keeps the entry form, the
// device code and the window's browser and focus.
public sealed partial class AccountsViewModel : ObservableObject
{
    private static readonly string[] Forwarded = [nameof(GitHubAccounts), nameof(GitLabAccounts), nameof(VaultText), nameof(Status)];
    private readonly AccountSettingsModel settings;

    public AccountsViewModel(AccountSettingsModel settings)
    {
        this.settings = settings;
        settings.PropertyChanged += (_, e) => { if (e.PropertyName is { } name && Forwarded.Contains(name)) OnPropertyChanged(name); };
        settings.Running.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Operation.IsBusy)) return;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            foreach (var command in new IRelayCommand[] { RetryStorageCommand, AddGitHubCommand, AddGitLabCommand, VerifyAccountCommand,
                ReplaceTokenCommand, RequestRemovalCommand, ConfirmRemovalCommand, KeepAccountCommand })
                command.NotifyCanExecuteChanged();
        };
    }

    public DeviceLoginPrompt Login { get; } = new();
    // The window supplies the browser; when the device page cannot be opened, the status says where to go by hand.
    public Func<Uri, bool> OpenBrowser { get; set; } = _ => false;
    public IReadOnlyList<AccountRow> GitHubAccounts => settings.GitHubAccounts;
    public IReadOnlyList<AccountRow> GitLabAccounts => settings.GitLabAccounts;
    public string VaultText => settings.VaultText;
    public string Status => settings.Status;
    [ObservableProperty] public partial string GitLabAddress { get; set; } = AccountMapping.GitLabDefault;
    [ObservableProperty]
    public partial bool MemoryOnly { get; set; }
    [ObservableProperty]
    public partial bool ShowGitLabEntry { get; set; }
    public bool IsBusy => settings.Running.IsBusy;
    public bool IsIdle => !IsBusy;

    public Task LoadAsync() => RunAsync(settings.LoadAsync());
    [RelayCommand(CanExecute = nameof(IsIdle))] private Task RetryStorageAsync() => LoadAsync();
    // The entry form does not open over an operation that runs.
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddGitLab()
    {
        if (IsBusy) return;
        ShowGitLabEntry = true;
        settings.Status = AccountText.GitLabTokenHelp;
    }
    // A new token for an account that is already connected: the entry form opens on that account's server with its
    // storage choice, so only the token has to be pasted.
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ReplaceToken(AccountInfo? account)
    {
        if (account is null || IsBusy)
            return;
        GitLabAddress = account.TransportOrigin.AbsoluteUri;
        MemoryOnly = !account.IsPersistent;
        AddGitLab();
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task AddGitHubAsync()
    {
        ShowGitLabEntry = false;
        return RunAsync(settings.SignInGitHubAsync(MemoryOnly, progress =>
        {
            if (Login.Apply(progress) is { } page) ShowDevicePage(page);
        }));
    }

    // The token stays in the view -> service boundary; only safe results enter the model.
    public Task RunGitLabEntryAsync(Func<CancellationToken, Task<AccountInfo>> signIn) => RunAsync(settings.SignInAsync(signIn));
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task VerifyAccountAsync(AccountInfo? account) => account is null ? Task.CompletedTask : RunAsync(settings.VerifyAsync(account));
    [RelayCommand(CanExecute = nameof(IsIdle))] private void RequestRemoval(AccountInfo? account) => settings.RequestRemoval(account);
    [RelayCommand(CanExecute = nameof(IsIdle))] private void KeepAccount() => settings.RequestRemoval(null);
    [RelayCommand(CanExecute = nameof(IsIdle))] private Task ConfirmRemovalAsync() => RunAsync(settings.ConfirmRemovalAsync());
    [RelayCommand] private void OpenDevicePage() => ShowDevicePage(new(AccountMapping.GitHubDevicePage));
    private void ShowDevicePage(Uri page)
    {
        if (!OpenBrowser(page))
            settings.Status = AccountText.OpenDevicePage;
    }
    [RelayCommand]
    private void Cancel()
    {
        settings.Cancel();
        if (!IsBusy)
            ShowGitLabEntry = false;
    }
    public void Close() => settings.Cancel();

    // An operation that ran leaves no device code behind, and one that connected an account closes the token entry.
    private async Task RunAsync(Task<AccountOutcome?> operation)
    {
        if (await operation is not { } outcome) return;
        if (outcome.SignedIn) ShowGitLabEntry = false;
        Login.Clear();
    }
}
