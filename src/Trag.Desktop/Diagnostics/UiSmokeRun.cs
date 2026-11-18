using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Trag.Desktop.Diagnostics.Probes;

namespace Trag.Desktop.Diagnostics;

// --ui-smoke and --ui-auth-smoke: the window opens, waits half a second, optionally runs the synthetic askpass check,
// prints TRAG_UI_SMOKE_OK and exits. A UI smoke check, not an input-to-ready performance measurement.
internal static class UiSmokeRun
{
    public static void Schedule(Window window, IClassicDesktopStyleApplicationLifetime desktop, bool auth) =>
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(500);
            if (auth)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await Task.Run(() => AuthProbe.RunCredentialsAsync(Environment.ProcessPath!, timeout.Token)); }
                catch (Exception) { Console.WriteLine("TRAG_UI_AUTH_FAILED"); desktop.Shutdown(1); return; }
            }
            Console.WriteLine("TRAG_UI_SMOKE_OK");
            desktop.Shutdown();
        }, DispatcherPriority.Background);
}
