using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Trag.Desktop.Platform;

// The title bar Windows 11 draws for a window that keeps it (Settings): its background takes the window's background
// and its title the window's text colour, so the bar follows the theme instead of Windows' own dark or light grey. Set
// through the Window style in Themes/Controls.axaml, so every window gets it; a no-op before Windows 11 and elsewhere.
public static partial class CaptionColor
{
    public static readonly AttachedProperty<bool> FollowsProperty = AvaloniaProperty.RegisterAttached<Window, bool>("Follows", typeof(CaptionColor));
    public static bool GetFollows(Window window) => window.GetValue(FollowsProperty);
    public static void SetFollows(Window window, bool value) => window.SetValue(FollowsProperty, value);

    private static readonly ConditionalWeakTable<Window, object> followed = new();
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    static CaptionColor() => FollowsProperty.Changed.AddClassHandler<Window>((window, e) => { if (e.NewValue is true) Follow(window); });

    // Windows takes a colour as COLORREF: red in the low byte, then green and blue.
    public static uint ColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);

    private static void Follow(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || followed.TryGetValue(window, out _)) return;
        followed.Add(window, new object());
        window.Opened += (_, _) => Apply(window);
        // A theme swaps the brushes the window's background and text are given.
        window.PropertyChanged += (_, e) => { if (e.Property == TemplatedControl.BackgroundProperty || e.Property == TemplatedControl.ForegroundProperty) Apply(window); };
        Apply(window);
    }

    private static void Apply(Window window)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd }) return;
        if (window.Background is ISolidColorBrush background) Set(hwnd, DWMWA_CAPTION_COLOR, background.Color);
        if (window.Foreground is ISolidColorBrush text) Set(hwnd, DWMWA_TEXT_COLOR, text.Color);
    }

    private static void Set(nint window, int attribute, Color color)
    {
        var value = ColorRef(color);
        DwmSetWindowAttribute(window, attribute, ref value, sizeof(uint));
    }

    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);
}
