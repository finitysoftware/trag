using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Trag.Desktop.UnitTests.TestApplication))]

namespace Trag.Desktop.UnitTests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
