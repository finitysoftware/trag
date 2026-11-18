using System.Globalization;
using System.Text;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Trag.Desktop.Diagnostics.Preview;
using Trag.Desktop.Views;
using Trag.Domain.Presentation;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Diagnostics.Probe;

// What every probe command works with: the window and its shell, the synthetic preview, the two report lines several
// commands share, pressing a button and looking for text in a diff, and the small conversions of the protocol (base64
// text, invariant numbers, 0/1 flags).
internal sealed class ProbeContext(MainWindow window, ShellViewModel shell, SyntheticPreview preview)
{
    public MainWindow Window => window;
    public ShellViewModel Shell => shell;
    public SyntheticPreview Preview => preview;
    public ProbeSurface Surface { get; } = new(window);

    public void Report(string operation) =>
        Console.WriteLine($"TRAG_{operation} {Flag(shell.Repository.HasRepository)} {shell.FileStatus.StagedFiles.Count} {shell.FileStatus.UnstagedFiles.Count} {Flag(shell.StatusBar.HasError)}");
    public void ReportHistory() =>
        Console.WriteLine($"TRAG_HISTORY {shell.History.Page.Commits.Count} {shell.History.Page.Number} {Flag(shell.History.Page.HasMore)} {Flag(shell.History.IsHistoryBusy)}");

    // A button pressed as a click presses it: nothing when its command cannot run; otherwise the command runs, an
    // asynchronous one to its end, and started is told once it runs, before that wait.
    public static async Task<bool> PressAsync(Button button, Action? started = null)
    {
        if (button.Command is not { } action || !action.CanExecute(null)) return false;
        if (action is IAsyncRelayCommand asyncAction)
        {
            var pending = asyncAction.ExecuteAsync(null);
            started?.Invoke();
            await pending;
        }
        else { action.Execute(null); started?.Invoke(); }
        return true;
    }

    public static bool Shows(DiffDocument document, string text) => document.Lines.Any(line => line.Text.Contains(text, StringComparison.Ordinal));

    public static string Flag(bool value) => value ? "1" : "0";
    public static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    public static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    public static double Double(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
