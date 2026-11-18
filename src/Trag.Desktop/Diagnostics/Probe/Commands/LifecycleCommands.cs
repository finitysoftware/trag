using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// The process and window: liveness, memory, size and placement, screenshots, theme, the repository session and cancel.
internal static class LifecycleCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("ping", () => Dispatcher.UIThread.Post(() => Console.WriteLine("TRAG_DISPATCH_ACK")));
        loop.Add("memory", () => Dispatcher.UIThread.Post(() =>
        {
            var info = GC.GetGCMemoryInfo();
            Console.WriteLine($"TRAG_MEMORY {GC.GetTotalMemory(false)} {info.HeapSizeBytes} {info.TotalCommittedBytes} {info.FragmentedBytes} {GC.CollectionCount(0)} {GC.CollectionCount(1)} {GC.CollectionCount(2)}");
        }));
        // The origin is a screen point in physical pixels; the size is the logical one a screenshot of the window has,
        // followed by the scaling that turns it into physical pixels, so the two never get compared across DPI.
        loop.Add("window-rect", () => Dispatcher.UIThread.Post(() =>
        {
            var origin = p.Window.PointToScreen(default);
            var size = Pixels(p.Window);
            Console.WriteLine(FormattableString.Invariant($"TRAG_WINDOW_RECT {origin.X} {origin.Y} {size.Width} {size.Height} {p.Window.RenderScaling:F2}"));
        }));
        loop.Add("size", rest =>
        {
            var parts = rest.Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                Dispatcher.UIThread.Post(async () =>
                {
                    p.Window.WindowState = WindowState.Normal;
                    await Task.Delay(100);
                    p.Window.Width = Math.Clamp(width, 880, 1920); p.Window.Height = Math.Clamp(height, 580, 1080);
                    await Task.Delay(100); Console.WriteLine("TRAG_SIZE_OK");
                });
        });
        loop.Add("screenshot", path => Dispatcher.UIThread.Post(async () =>
        {
            Window target = (Window?)p.Window.SettingsHost.ActiveSettings ?? p.Window;
            var bitmap = new RenderTargetBitmap(Pixels(target));
            bitmap.Render(target);
            await Task.Run(() => { using (bitmap) bitmap.Save(path, PngBitmapEncoderOptions.Default); });
            Console.WriteLine("TRAG_SCREENSHOT_SAVED");
        }));
        loop.Add("theme", rest =>
        {
            var theme = Theme(rest);
            Dispatcher.UIThread.Post(() => { p.Shell.Layout.ThemeIndex = (int)theme; Console.WriteLine("TRAG_THEME_OK"); });
        });
        loop.Add("open", path => Dispatcher.UIThread.Post(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await p.Shell.Session.OpenRepositoryAsync(path, timeout.Token);
            p.Report("OPEN");
        }));
        loop.Add("refresh", () => Dispatcher.UIThread.Post(async () => { await p.Shell.RefreshCommand.ExecuteAsync(null); p.Report("REFRESH"); }));
        loop.Add("restore", () => Dispatcher.UIThread.Post(async () => { await p.Shell.InitializeAsync(); p.Report("RESTORE"); }));
        loop.Add("close", () => Dispatcher.UIThread.Post(async () => { await p.Shell.CloseRepositoryCommand.ExecuteAsync(null); p.Report("CLOSE"); }));
        loop.Add("cancel", () => Dispatcher.UIThread.Post(() => { p.Shell.CancelCommand.Execute(null); Console.WriteLine("TRAG_CANCEL_OK"); }));
    }

    // "dark" is Graphite, the dark theme a dark Windows gives the system choice; any other name must be a theme's own.
    public static ThemeChoice Theme(string name) =>
        name == "dark" ? ThemeChoice.DarkGraphite : Enum.Parse<ThemeChoice>(name, ignoreCase: true);

    // A screenshot is the window at its logical size, one pixel per unit, whatever the display's scaling.
    private static PixelSize Pixels(Window window) => new((int)window.Bounds.Width, (int)window.Bounds.Height);
}
