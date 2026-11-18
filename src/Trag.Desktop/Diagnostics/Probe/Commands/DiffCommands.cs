using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// The diff control: mode, row selection, side ratio, preference load/save and its state line; a text check on the shown
// working diff.
internal static class DiffCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("diff-ui", rest => Dispatcher.UIThread.Post(async () => await ReportAsync(p, rest)));
        loop.Add("diff-contains", text => Dispatcher.UIThread.Post(() =>
            Console.WriteLine("TRAG_DIFF_CONTAINS " + ProbeContext.Flag(!p.Shell.WorkingDiff.Diff.IsBusy && ProbeContext.Shows(p.Shell.WorkingDiff.Diff.Document, text)))));
    }

    // One diff-ui command: the action on the visible diff view once its reads are done, then the state line with the
    // screen point (physical pixels) three quarters across the rows, where native wheel input is aimed.
    private static async Task ReportAsync(ProbeContext p, string rest)
    {
        var parts = rest.Split(' ');
        await p.Shell.WorkingDiff.Diff.Completion; await p.Shell.History.Details.Diff.Completion;
        p.Window.UpdateLayout();
        var view = p.Surface.VisibleDiffView;
        var rows = ProbeSurface.Find<ListBox>(view, "Rows");
        await ApplyAsync(p, view, rows, parts[0], parts.Length > 1 ? parts[1] : "");
        await Dispatcher.UIThread.InvokeAsync(() => p.Window.UpdateLayout(), DispatcherPriority.Background);
        var point = TopLevel.GetTopLevel(rows) is null ? p.Window.PointToScreen(default)
            : rows.PointToScreen(new(rows.Bounds.Width * 0.75, rows.Bounds.Height * 0.5));
        Console.WriteLine(FormattableString.Invariant($"TRAG_DIFF_UI {view.Mode} {view.Document.Lines.Count} {view.Document.SplitRows.Count} {view.SelectedSource} {view.TopSource} {rows.GetRealizedContainers().Count()} {view.SplitRatio:F3} {point.X} {point.Y}"));
    }

    private static async Task ApplyAsync(ProbeContext p, DiffView view, ListBox rows, string action, string value)
    {
        switch (action)
        {
            case "mode": ProbeSurface.Find<ComboBox>(view, "DiffModeSelector").SelectedIndex = ProbeContext.Int(value); break;
            case "go": rows.SelectedIndex = ProbeContext.Int(value); rows.ScrollIntoView(rows.SelectedItem!); rows.Focus(); break;
            case "ratio": ProbeSurface.Find<RatioGrid>(view, "SideHeaders").Ratio = ProbeContext.Double(value); break;
            case "load": await p.Shell.Layout.LoadLayoutAsync(); break;
            case "save": await p.Shell.Layout.SaveCompletion; break;
        }
    }
}
