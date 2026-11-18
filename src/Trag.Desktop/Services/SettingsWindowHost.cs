using Avalonia.Controls;
using Trag.Application.Accounts;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Platform;
using Trag.Application.Live;
using Trag.Application.Setup;
using Trag.Application.Ports.Accounts;
using Trag.Application.Ports.Updates;
using Trag.Application.Updates;
using Trag.ViewModels.Accounts;
using Trag.ViewModels.Setup;
using Trag.ViewModels.Sync;
using Trag.ViewModels.Updates;

namespace Trag.Desktop.Services;

// The settings window as a singleton over the main window: one instance at a time, activated when asked again.
// The account service behind it is set by the composition root; the probes swap in a synthetic one.
public sealed class SettingsWindowHost(Window owner, ShellLauncher launcher)
{
    public IAccountService? AccountService { get; set; }
    // The automatic fetch Settings offers; none in the probes and tests that open Settings on their own.
    public AutoFetchViewModel? AutoFetch { get; set; }
    // The folder crash logs go to; none in the probes that keep no files.
    public string? CrashLogs { get; set; }
    // The Git the shell runs, which Settings shows and lets the user choose.
    public GitSetup? Git { get; set; }
    // The update service About asks when the user wants to know; none in the probes, which ask no service.
    public IUpdateChecker? Updates { get; set; }
    public SettingsWindow? ActiveSettings { get; private set; }

    public async Task<bool> ShowSettingsAsync(LayoutPreferences? layout = null)
    {
        if (ActiveSettings is { } current) { current.Activate(); return false; }
        if (AccountService is null) return false;
        // The launcher opens HTTPS addresses only; the model decides which page is worth opening and what to say when it fails.
        var accounts = new AccountsViewModel(new AccountSettingsModel(new AccountOperations(AccountService), new ViewDispatcher())) { OpenBrowser = launcher.OpenBrowser };
        var settings = new SettingsWindow(accounts, AccountService, AutoFetch, layout, Git is { } git ? new GitSettingsViewModel(new GitSettingsModel(git)) : null);
        settings.About.CrashLogs = CrashLogs; settings.About.Reveal = path => launcher.Reveal(path); settings.About.Open = path => launcher.Open(path); settings.About.OpenBrowser = launcher.OpenBrowser;
        if (Updates is { } updates) settings.About.Update = new UpdateViewModel(new UpdateModel(updates, BuildVersion.Informational)) { OpenBrowser = launcher.OpenBrowser };
        ActiveSettings = settings;
        settings.Closed += (_, _) => ActiveSettings = null;
        await settings.ShowDialog(owner);
        return true;
    }
}
