using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Trag.Desktop.Controls;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// The desktop layout: section, theme and focus state, a control's placement and pane ratio, and the synthetic preview. The registrations are one flat
// list of names; what each one does lives in the method beside it.
internal static class LayoutCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("desktop-state", () => Dispatcher.UIThread.Post(() => ReportState(p)));
        loop.Add("desktop-control", name => Dispatcher.UIThread.Post(() => ReportControl(p, name)));
        loop.Add("preview", () => Dispatcher.UIThread.Post(p.Preview.Toggle));
    }

    private static void ReportState(ProbeContext p)
    {
        p.Window.UpdateLayout();
        var focus = (p.Window.FocusManager?.GetFocusedElement() as Control)?.Name ?? "none";
        var model = p.Shell;
        Console.WriteLine(FormattableString.Invariant($"TRAG_DESKTOP {model.Navigator.Section} {model.Layout.ThemeIndex} {p.Window.RenderScaling:F2} {p.Window.ActualThemeVariant} {focus} {model.Layout.DiffFilesRatio:R} {model.Layout.HistoryPanelRatio:R} {model.Layout.StagePanelRatio:R} {model.FileStatus.HasSelection} {model.History.Details.Panel.HasSelection}"));
    }

    // The control's centre in physical pixels from the window's origin (what native pointer input takes), its
    // logical size, and the ratio and pane lengths of the RatioGrid it sits in, when it sits in one.
    private static void ReportControl(ProbeContext p, string name)
    {
        p.Window.UpdateLayout();
        var control = p.Surface.Find<Control>(name);
        var point = control.PointToScreen(new(control.Bounds.Width / 2, control.Bounds.Height / 2));
        var origin = p.Window.PointToScreen(default);
        var (ratio, first, last) = PaneOf(control);
        Console.WriteLine(FormattableString.Invariant($"TRAG_CONTROL {name} {point.X - origin.X} {point.Y - origin.Y} {control.Bounds.Width:F2} {control.Bounds.Height:F2} {ratio:R} {first:F2} {last:F2}"));
    }

    private static (double Ratio, double First, double Last) PaneOf(Control control)
    {
        if (control.Parent is not RatioGrid pane) return (-1, 0, 0);
        var vertical = pane.Orientation == Avalonia.Layout.Orientation.Vertical;
        var first = vertical ? pane.Children[0].Bounds.Height : pane.Children[0].Bounds.Width;
        var last = vertical ? pane.Children[2].Bounds.Height : pane.Children[2].Bounds.Width;
        return (pane.Ratio, first, last);
    }
}
