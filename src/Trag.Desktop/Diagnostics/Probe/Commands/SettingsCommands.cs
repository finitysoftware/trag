using Avalonia.Threading;
using Trag.Desktop.Diagnostics.Fixtures;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// The settings window over a synthetic account service: opening it through the toolbar button, filling it with
// fixture accounts, the GitLab entry card and closing it. Each step answers TRAG_SETTINGS_OK once done.
internal static class SettingsCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("settings-open", () => Dispatcher.UIThread.Post(async () => { await OpenAsync(p); Console.WriteLine("TRAG_SETTINGS_OK"); }));
        loop.Add("settings-fixture", () => Dispatcher.UIThread.Post(async () => { await PopulateAsync(p); Console.WriteLine("TRAG_SETTINGS_OK"); }));
        loop.Add("settings-gitlab", () => Dispatcher.UIThread.Post(() => { p.Window.SettingsHost.ActiveSettings!.Model.AddGitLabCommand.Execute(null); Console.WriteLine("TRAG_SETTINGS_OK"); }));
        loop.Add("settings-close", () => Dispatcher.UIThread.Post(() => { Close(p); Console.WriteLine("TRAG_SETTINGS_OK"); }));
    }

    // The synthetic service goes in first; then Settings, as More offers it, opens the window.
    private static async Task OpenAsync(ProbeContext p)
    {
        var settingsHost = p.Window.SettingsHost;
        settingsHost.AccountService = SyntheticAccountService.Create();
        p.Shell.Catalog.RequestSettingsCommand.Execute(null);
        await settingsHost.ActiveSettings!.Initialization;
    }

    private static async Task PopulateAsync(ProbeContext p)
    {
        if (p.Window.SettingsHost.ActiveSettings is { } settings) await SettingsFixture.PopulateAsync(settings, p.Window.SettingsHost);
    }

    private static void Close(ProbeContext p)
    {
        var settingsHost = p.Window.SettingsHost;
        settingsHost.ActiveSettings?.Close();
        (settingsHost.AccountService as IDisposable)?.Dispose();
    }
}
