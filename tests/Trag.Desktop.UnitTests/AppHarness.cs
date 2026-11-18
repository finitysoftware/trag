using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Platform;
using Trag.Desktop.Views;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests;

// The shell harness plus what only a headless window needs: the real dispatcher, the main window over the shell, the
// account service the window's dialogs use, and the layout and pointer helpers every UI test shares.
internal sealed class AppHarness : ShellHarness
{
    public FakeAccountService Accounts { get; } = new();

    public AppHarness() => Dispatcher = new ViewDispatcher();

    public MainWindow Window(ShellViewModel model)
    {
        var window = new MainWindow { DataContext = model, Width = 1000, Height = 700, SettingsHost = { AccountService = Accounts } };
        window.Show();
        Layout(window);
        return window;
    }

    public static void Layout(Window window) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    // The shared wait, pumping the UI thread between checks so posted work and timers can run.
    public static new Task Until(Func<bool> condition, string what) =>
        ShellHarness.Until(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return condition(); }, what);

    public static void Drag(Window window, Control handle, double x, double y)
    {
        var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(x / 2, y / 2)); Layout(window);
        window.MouseMove(start + new Vector(x, y)); window.MouseUp(start + new Vector(x, y), MouseButton.Left); Layout(window);
    }

    // The space between the right edge of one control and the left edge of the next, in the window.
    public static double Gap(Window window, Control left, Control right) =>
        right.TranslatePoint(default, window)!.Value.X - left.TranslatePoint(new Point(left.Bounds.Width, 0), window)!.Value.X;

    // The keyboard's two ways to ask the focused control for its menu: the menu key, or Shift+F10. The Windows platform
    // adds Shift+F10 to the menu key's gestures when it starts, the headless one knows the menu key alone, so the
    // gesture is added for the press.
    public static void AskForMenu(Window window, string keys)
    {
        if (keys == "Menu")
        {
            window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
            window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
            return;
        }
        var gestures = window.GetPlatformSettings()!.HotkeyConfiguration.OpenContextMenu;
        var shiftF10 = new KeyGesture(Key.F10, KeyModifiers.Shift);
        gestures.Add(shiftF10);
        try
        {
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            window.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
        }
        finally { gestures.Remove(shiftF10); }
    }

    public static void DoubleTap(Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Layout(window);
    }
}
