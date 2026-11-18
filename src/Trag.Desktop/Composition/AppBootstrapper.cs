using System.Runtime.CompilerServices;
using Avalonia;

namespace Trag.Desktop.Composition;

// Builds the Avalonia application for a desktop launch: software rendering onto a redirection surface on Windows.
public static class AppBootstrapper
{
    // Kept out of Main's body so the askpass and probe branches never load Avalonia.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RunUi(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .With(new Win32PlatformOptions
        {
            RenderingMode = [Win32RenderingMode.Software],
            CompositionMode = [Win32CompositionMode.RedirectionSurface]
        })
        .UsePlatformDetect();
}
