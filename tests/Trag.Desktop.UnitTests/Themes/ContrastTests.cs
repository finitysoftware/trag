using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Trag.Desktop.Themes;

namespace Trag.Desktop.UnitTests.Themes;

// Every colour the app draws text and icons in stands off every background it draws them on by at least 4.5:1, the
// WCAG AA ratio for text, in each theme's dictionary. System is White or Graphite, whichever Windows uses.
public sealed class ContrastTests
{
    private static readonly string[] Foregrounds = ["TextBrush", "MutedBrush", "AccentBrush", "DiffAddedBrush", "DiffRemovedBrush", "WarningTextBrush", "MineBrush", "TheirsBrush"];
    private static readonly string[] Backgrounds = ["ChromeBrush", "SurfaceBrush", "DialogBrush", "ControlBrush", "HoverBrush", "SelectedBrush",
        "SettingsSurface", "SettingsCard", "SettingsSelected", "SettingsHover"];

    [AvaloniaTheory]
    [InlineData("White")]
    [InlineData("Graphite")]
    [InlineData("Espresso")]
    [InlineData("Obsidian")]
    [InlineData("Vantablack")]
    public void WhenTheThemeDrawsTextOnAnyOfItsBackgrounds_ShouldKeepTheWcagRatioForText(string name)
    {
        var theme = name switch
        {
            "White" => ThemeVariant.Light,
            "Graphite" => ThemeVariant.Dark,
            "Espresso" => AppThemes.DarkEspresso,
            "Obsidian" => AppThemes.DarkObsidian,
            _ => AppThemes.DarkVantablack,
        };

        var low = from foreground in Foregrounds
                  from background in Backgrounds
                  let ratio = Ratio(ColourOf(foreground, theme), ColourOf(background, theme))
                  where ratio < 4.5
                  select $"{foreground} on {background}: {ratio:F2}";

        Assert.Empty(low);
    }

    private static double Ratio(Color a, Color b)
    {
        var (lighter, darker) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color colour) => 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color ColourOf(string key, ThemeVariant theme)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, theme, out var brush), key);
        return ((ISolidColorBrush)brush!).Color;
    }
}
