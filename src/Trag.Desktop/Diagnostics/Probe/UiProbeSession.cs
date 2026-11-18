using System.Text;
using Avalonia.Input;
using Avalonia.Threading;
using Trag.Desktop.Diagnostics.Preview;
using Trag.Desktop.Diagnostics.Probe.Commands;
using Trag.Desktop.Views;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Diagnostics.Probe;

// Probe-only control channel over stdin/stdout (--ui-probe). No credentials; never enabled on a normal launch.
// The TRAG_* lines the commands print are the protocol Trag.Benchmarks drives.
internal static class UiProbeSession
{
    public static void Attach(MainWindow window, ShellViewModel model, SyntheticPreview preview, Action<int> shutdown)
    {
        window.Closing += (_, e) => Console.WriteLine($"TRAG_WINDOW_CLOSING {e.CloseReason} {e.IsProgrammatic} {e.Cancel}");
        Console.OutputEncoding = new UTF8Encoding(false);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F8) { Console.WriteLine("TRAG_INPUT_ACK"); e.Handled = true; }
        };
        var context = new ProbeContext(window, model, preview);
        var loop = new ProbeCommandLoop();
        LifecycleCommands.Register(loop, context);
        LayoutCommands.Register(loop, context);
        DiffCommands.Register(loop, context);
        StatusCommands.Register(loop, context);
        HistoryCommands.Register(loop, context);
        SyncCommands.Register(loop, context);
        MergeCommands.Register(loop, context);
        SettingsCommands.Register(loop, context);
        window.Opened += (_, _) =>
        {
            window.Focus();
            loop.Start(() => Dispatcher.UIThread.Post(() => shutdown(0)));
        };
    }
}
