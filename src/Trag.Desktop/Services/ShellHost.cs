using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Trag.Desktop.Platform;
using Trag.Desktop.Views;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Services;

// The glue between the window and its shell: answers the shell's host requests (settings, reveal, browser, files) with the
// window's services while the window shows that shell, announces settings closure, and asks the workbench whether the
// window may close (not during a Git write; a pending layout save is awaited first).
public sealed class ShellHost : IShellHost
{
    private readonly MainWindow window;
    private readonly SettingsWindowHost settingsHost;
    private readonly ShellLauncher launcher;
    private ShellViewModel? shell;
    private bool savingLayoutBeforeClose;

    public ShellHost(MainWindow window, SettingsWindowHost settingsHost, ShellLauncher launcher)
    {
        this.window = window; this.settingsHost = settingsHost; this.launcher = launcher;
        window.DataContextChanged += DataContextChanged;
        window.Closing += Closing;
        window.Closed += Closed;
        Follow(window.DataContext as ShellViewModel);
    }

    private void DataContextChanged(object? sender, EventArgs e) => Follow(window.DataContext as ShellViewModel);

    private void Closed(object? sender, EventArgs e)
    {
        Follow(null);
        window.DataContextChanged -= DataContextChanged;
        window.Closing -= Closing;
        window.Closed -= Closed;
    }

    // The window's shell right now; the host moves with it when the DataContext is swapped.
    private void Follow(ShellViewModel? model)
    {
        if (ReferenceEquals(shell, model)) return;
        if (shell is { } previous) previous.Host = null;
        shell = model;
        if (model is not null) model.Host = this;
    }

    public bool Reveal(string path)
    {
        if (!Directory.Exists(path)) return false;
        Say(launcher.Reveal(path), ShellText.FileManagerNotStarted);
        return true;
    }
    public void OpenBrowser(Uri uri) => Say(launcher.OpenBrowser(uri), ShellText.BrowserNotStarted);
    public void ShowSettings() => _ = ShowSettingsAsync();
    public void OpenTerminal(string path) => Say(launcher.OpenTerminal(path), ShellText.TerminalNotStarted);

    public bool OpenFile(string path)
    {
        if (!File.Exists(path)) return false;
        Say(launcher.Open(path), ShellText.FileNotOpened);
        return true;
    }

    public bool RevealFile(string path)
    {
        if (!File.Exists(path)) return false;
        Say(launcher.RevealFile(path), ShellText.FileManagerNotStarted);
        return true;
    }

    // What the system could not start is said in the status bar of the shell the window shows.
    private void Say(bool started, string notice)
    {
        if (!started) shell!.StatusBar.Notice = notice;
    }

    public void CopyText(string text) => _ = window.Clipboard?.SetTextAsync(text);

    public async Task<string?> PickSaveFileAsync(string fileName)
    {
        using var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = FileVersionText.SaveTitle, SuggestedFileName = fileName, ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public Task<string?> PickGitExecutableAsync() => GitExecutablePicker.PickAsync(window.StorageProvider);

    // The focus goes back where it was once the dialog closes.
    public async Task<string?> PickFolderAsync()
    {
        var focused = window.FocusManager?.GetFocusedElement();
        var provider = window.StorageProvider;
        if (!provider.CanPickFolder) throw new NotSupportedException("Folder picking is unavailable.");
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = CatalogText.ChooseFolder, AllowMultiple = false });
        try
        {
            if (folders.Count == 0) return null;
            return folders[0].TryGetLocalPath() ?? throw new NotSupportedException("Only local folders are supported.");
        }
        finally { foreach (var folder in folders) folder.Dispose(); focused?.Focus(); }
    }

    // Settings change the theme of the shell this window shows; once they close, the accounts are no longer being edited,
    // and the keyboard goes back to what had it when they opened, or to More when nothing can take it.
    public async Task ShowSettingsAsync()
    {
        var focused = window.FocusManager?.GetFocusedElement();
        if (!await settingsHost.ShowSettingsAsync(shell?.Layout)) return;
        shell?.Workbench.Accounts.EndEditing();
        if (focused?.Focus() != true) window.FocusMore();
    }

    private async void Closing(object? sender, WindowClosingEventArgs e)
    {
        if (shell is not { } model) return;
        if (model.Workbench.RefusesToClose())
            e.Cancel = true;
        else if (!model.Workbench.Saving.IsCompleted)
        {
            e.Cancel = true;
            if (savingLayoutBeforeClose) return;
            savingLayoutBeforeClose = true;
            await model.Workbench.Saving;
            savingLayoutBeforeClose = false;
            window.Close();
        }
    }
}
