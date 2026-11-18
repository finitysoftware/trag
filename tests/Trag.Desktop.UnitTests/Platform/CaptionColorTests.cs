using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Platform;

namespace Trag.Desktop.UnitTests.Platform;

// A window with Windows' own title bar (Settings) colours that bar after the theme: its background and title text are
// the window's, given to Windows as a COLORREF (red in the low byte).
public sealed class CaptionColorTests
{
    [Fact]
    public void WhenAColourIsGivenToWindows_ShouldBeAColorRef() => Assert.Equal(0x002C241Eu, CaptionColor.ColorRef(Color.Parse("#1E242C")));

    [AvaloniaFact]
    public void WhenAWindowShows_ShouldColourItsTitleBarAfterTheTheme()
    {
        var window = new SettingsWindow(); window.Show(); AppHarness.Layout(window);
        Assert.True(CaptionColor.GetFollows(window));
        window.Close();
    }
}
