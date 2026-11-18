using Avalonia.Controls;
using Avalonia.Interactivity;
using Trag.Desktop.Platform;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Updates;
using Trag.ViewModels.Updates;

namespace Trag.Desktop.Dialogs.Settings;

public sealed partial class AboutPage : UserControl
{
    private string? crashLogs;

    public AboutPage()
    {
        InitializeComponent();
        AppVersion.Text = AboutText.Version(BuildVersion.Informational);
    }

    // The folder crash logs go to; a probe that keeps no files has none, and the card is not shown.
    public string? CrashLogs
    {
        get => crashLogs;
        set { crashLogs = value; CrashLogsPath.Text = value; CrashLogsCard.IsVisible = value is not null; }
    }

    // The update check the host hands in; a probe asks no service, and About offers no check.
    public UpdateViewModel? Update
    {
        get => UpdateCard.DataContext as UpdateViewModel;
        set { UpdateCard.DataContext = value; UpdateLine.IsVisible = UpdateActions.IsVisible = value is not null; }
    }

    // Show a folder in the file manager, open a file in the program the system has for it, and an HTTPS address in the
    // browser; the host hands in the shell's.
    public Action<string>? Reveal { get; set; }
    public Action<string>? Open { get; set; }
    public Func<Uri, bool>? OpenBrowser { get; set; }

    private void ShowFinitySoftware(object? sender, RoutedEventArgs e) => OpenBrowser!(FinitySoftware.Website);

    // The notices ship beside the program.
    private void ShowLicense(object? sender, RoutedEventArgs e) => Open!(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"));
    private void ShowNotices(object? sender, RoutedEventArgs e) => Open!(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));

    // Until the first crash the folder is not there, so it is made for the file manager to open; one that cannot be made,
    // as the crash log itself finds, is not shown.
    private void ShowCrashLogs(object? sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(crashLogs!); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { return; }
        Reveal!(crashLogs!);
    }
}
