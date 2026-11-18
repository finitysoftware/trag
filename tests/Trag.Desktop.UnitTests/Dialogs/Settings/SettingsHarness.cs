using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Application.Ports.Accounts;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Platform;
using Trag.ViewModels.Accounts;
using Trag.ViewModels.Setup;

namespace Trag.Desktop.UnitTests.Dialogs.Settings;

// The settings window over the account service, its model composed as the host composes it, with no browser to open.
internal static class SettingsHarness
{
    public static SettingsWindow Window(IAccountService service, LayoutPreferences? layout = null, GitSettingsViewModel? git = null) =>
        new(new AccountsViewModel(new AccountSettingsModel(new AccountOperations(service), new ViewDispatcher())), service, layout: layout, git: git);

    // A real click on a section in the left list.
    public static void Choose(SettingsWindow window, string section) => Click(window, window.Descendant<RadioButton>(section)!);

    // A real click in the middle of a control.
    public static void Click(SettingsWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left); window.MouseUp(centre, MouseButton.Left); AppHarness.Layout(window);
    }
}
