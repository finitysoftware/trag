using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.UnitTests.Dialogs.Settings;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Dialogs;

// The ✕ every dialog's header ends with is a square, as wide as it is tall, wherever the dialog is.
public sealed class DialogCloseButtonTests
{
    // The header has three looks: a round plate with the icon and the subtitle beside it, a plain icon, or no icon at all.
    [AvaloniaFact]
    public void WhenADialogNamesAPlateAnIconOrNeither_ShouldShowTheHeaderThatGoesWithIt()
    {
        static (bool Plate, bool Icon, double Spacing, Avalonia.Layout.VerticalAlignment Close, bool Subtitle) Header(DialogHost host)
        {
            var window = new Window { Content = host };
            window.Show(); AppHarness.Layout(window);
            var header = (window.Descendant<Border>("Plate")!.IsVisible, window.Descendant<Trag.Desktop.Controls.LineIcon>("PlainIcon")!.IsVisible,
                window.Descendant<Grid>("Header")!.ColumnSpacing, window.Descendant<Button>("CloseButton")!.VerticalAlignment,
                window.Descendant<TextBlock>("SubtitleText")!.IsVisible);
            window.Close();
            return header;
        }

        Assert.Equal((true, false, 14, Avalonia.Layout.VerticalAlignment.Top, true),
            Header(new DialogHost { IsOpen = true, Icon = "branch", PlateBrush = Avalonia.Media.Brushes.Gray, Subtitle = "Subtitle" }));
        Assert.Equal((false, true, 12, Avalonia.Layout.VerticalAlignment.Center, false), Header(new DialogHost { IsOpen = true, Icon = "branch", Subtitle = "Subtitle" }));
        Assert.Equal((false, false, 0, Avalonia.Layout.VerticalAlignment.Center, false), Header(new DialogHost { IsOpen = true, Subtitle = "Subtitle" }));
    }

    [AvaloniaFact]
    public void WhenADialogShows_ShouldCloseItWithASquareButton()
    {
        var window = new Window { Content = new RemotesDialog { IsOpen = true } };
        window.Show(); AppHarness.Layout(window);

        var close = window.Descendant<Button>("CloseButton")!.Bounds;

        Assert.True(close.Width == close.Height, $"the close button is {close.Width} wide and {close.Height} tall");
        window.Close();
    }

    // Settings sizes its own buttons; the ✕ of a dialog over it keeps the whole square for its glyph, as anywhere else.
    [AvaloniaFact]
    public async Task WhenADialogShowsOverSettings_ShouldDrawItsCloseButtonAsAnywhereElse()
    {
        var plain = new Window { Content = new RemotesDialog { IsOpen = true } };
        plain.Show(); AppHarness.Layout(plain);
        var settings = SettingsHarness.Window(new FakeAccountService()); settings.Show(); await settings.Initialization;
        settings.FindControl<DeviceLoginDialog>("DeviceLoginOverlay")!.IsOpen = true; AppHarness.Layout(settings);

        var (expected, close) = (plain.Descendant<Button>("CloseButton")!, settings.Descendant<Button>("CloseButton")!);

        Assert.Equal((expected.Padding, expected.FontSize), (close.Padding, close.FontSize));
        settings.Close(); plain.Close();
    }
}
