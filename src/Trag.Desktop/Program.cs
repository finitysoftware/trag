using Trag.Desktop.Composition;
using Trag.Desktop.Diagnostics.Probes;

namespace Trag.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var mode = ModeSelector.Select(args, Environment.GetEnvironmentVariable);
        // The askpass branch must stay ahead of every call that can touch Avalonia/Skia.
        return mode.Kind switch
        {
            LaunchKind.AskPass => AskPassEntryPoint.Run(args),
            LaunchKind.Probe => Phase0Probes.RunAsync(args).GetAwaiter().GetResult(),
            _ => AppBootstrapper.RunUi(args)
        };
    }

    // The Avalonia designer builds the application from here; the headless tests have their own BuildAvaloniaApp.
    public static Avalonia.AppBuilder BuildAvaloniaApp() => AppBootstrapper.BuildAvaloniaApp();
}
