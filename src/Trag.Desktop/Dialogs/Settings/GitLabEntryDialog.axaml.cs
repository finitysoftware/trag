using System.Security.Cryptography;
using System.Text;
using Avalonia.Interactivity;
using Trag.Application.Ports.Accounts;
using Trag.ViewModels.Accounts;

namespace Trag.Desktop.Dialogs.Settings;

public sealed partial class GitLabEntryDialog : DialogHost
{
    // The sign-in goes straight to the service so the token bytes never pass through the view model; the window supplies it.
    public IAccountService? Service { get; set; }
    private AccountsViewModel? Model => DataContext as AccountsViewModel;
    public GitLabEntryDialog() => InitializeComponent();
    public void ClearToken() => GitLabTokenEditor.Text = "";
    private async void ConnectGitLab(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || Service is not { } service || model.IsBusy) return;
        var bytes = Encoding.UTF8.GetBytes(GitLabTokenEditor.Text ?? "");
        GitLabTokenEditor.Text = "";
        var address = model.GitLabAddress; var memoryOnly = model.MemoryOnly;
        try { await model.RunGitLabEntryAsync(ct => service.SignInGitLabAsync(address, bytes, memoryOnly, ct)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
