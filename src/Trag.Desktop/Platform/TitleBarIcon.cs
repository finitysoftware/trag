using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Trag.Desktop.Platform;

// The icon Windows draws in a native title bar (ICON_SMALL), kept apart from Window.Icon, which still feeds the taskbar,
// Alt+Tab and Task Manager (ICON_BIG). Set through the Window style in Themes/Controls.axaml, so every window gets it; a no-op elsewhere.
public static partial class TitleBarIcon
{
    public static readonly AttachedProperty<string?> SourceProperty = AvaloniaProperty.RegisterAttached<Window, string?>("Source", typeof(TitleBarIcon));
    public static string? GetSource(Window window) => window.GetValue(SourceProperty);
    public static void SetSource(Window window, string? value) => window.SetValue(SourceProperty, value);

    private static readonly AttachedProperty<nint> HandleProperty = AvaloniaProperty.RegisterAttached<Window, nint>("Handle", typeof(TitleBarIcon));
    private static readonly ConditionalWeakTable<Window, object> followed = new();
    private static readonly Dictionary<string, byte[]> files = new();
    private const uint WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0;
    private const int SM_CXSMICON = 49;

    static TitleBarIcon() => SourceProperty.Changed.AddClassHandler<Window>((window, _) => Follow(window));

    private static void Follow(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!followed.TryGetValue(window, out _))
        {
            followed.Add(window, new object());
            window.Opened += (_, _) => Apply(window);
            window.ScalingChanged += (_, _) => Apply(window);                                              // another DPI wants another pixel size
            window.PropertyChanged += (_, e) => { if (e.Property == Window.IconProperty) Apply(window); }; // Avalonia resets both icons when Icon changes
            window.Closed += (_, _) => Release(window);
        }
        Apply(window);
    }

    private static unsafe void Apply(Window window)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd } || GetSource(window) is not { Length: > 0 } source) return;
        var size = GetSystemMetricsForDpi(SM_CXSMICON, (uint)Math.Round(96 * window.RenderScaling));
        var image = PickEntry(Load(source), size);
        nint icon;
        fixed (byte* bits = image) icon = CreateIconFromResourceEx(bits, (uint)image.Length, 1, 0x30000, size, size, 0);
        if (icon == 0) return;
        SendMessageW(hwnd, WM_SETICON, ICON_SMALL, icon);
        Release(window);
        window.SetValue(HandleProperty, icon);
    }

    private static void Release(Window window)
    {
        var icon = window.GetValue(HandleProperty);
        if (icon == 0) return;
        window.SetValue(HandleProperty, 0);
        DestroyIcon(icon);
    }

    private static byte[] Load(string source)
    {
        if (files.TryGetValue(source, out var bytes)) return bytes;
        using var stream = AssetLoader.Open(new Uri("avares://Trag" + source));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return files[source] = buffer.ToArray();
    }

    // The .ico image that best serves the requested pixel size: the smallest one at least that big, else the largest there is.
    public static ReadOnlySpan<byte> PickEntry(byte[] ico, int wanted)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)), best = 0, bestScore = int.MaxValue;
        for (var i = 0; i < count; i++)
        {
            int size = ico[6 + i * 16] == 0 ? 256 : ico[6 + i * 16], score = size >= wanted ? size - wanted : 1000 + wanted - size;
            if (score < bestScore) { best = i; bestScore = score; }
        }
        var entry = ico.AsSpan(6 + best * 16, 16);
        return ico.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
    }

    [LibraryImport("user32.dll")] private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static unsafe partial nint CreateIconFromResourceEx(byte* bits, uint length, int isIcon, uint version, int width, int height, uint flags);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll")] private static partial int DestroyIcon(nint icon);
}
