using Avalonia.Controls;

namespace Trag.Desktop.Platform;

// On Windows the client area extends into the title bar: Avalonia draws the caption buttons and the window title, the
// window's own TitleBar row (this tall) supplies the background, the app icon and the repository name. Other platforms
// keep their native frame; the same row then sits below it and also names the app.
public static class WindowChrome
{
    public const double TitleBarHeight = 30;

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
    }
}
