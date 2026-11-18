using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Themes;

namespace Trag.Desktop.UnitTests.Themes;

// The four dark themes. Graphite, cool graphite with orange accents, is Avalonia's Dark: the theme a dark Windows
// gives the system choice. Espresso, the warm theme Trag had first, Obsidian, near-black, and Vantablack, black, are
// drawn from it: each names its own colour for every brush Dark names, so none of Graphite's shows through, and draws
// as Dark wherever neither names one, Fluent's controls included.
public sealed class DarkThemesTests
{
    [AvaloniaTheory]
    [InlineData("SurfaceBrush", "#14171C")]
    [InlineData("ChromeBrush", "#1B2027")]
    [InlineData("DialogBrush", "#1E242C")]
    [InlineData("SettingsSurface", "#1E242C")]
    [InlineData("ControlBrush", "#252D37")]
    [InlineData("SettingsCard", "#252D37")]
    [InlineData("DividerBrush", "#35404D")]
    [InlineData("SettingsLine", "#35404D")]
    [InlineData("TextBrush", "#E6EAF0")]
    [InlineData("MutedBrush", "#A3ADBA")]
    [InlineData("AccentBrush", "#E38D5D")]
    [InlineData("AccentStrongBrush", "#E07A3F")]
    [InlineData("TextControlBackground", "#14171C")]
    [InlineData("TextControlBackgroundPointerOver", "#14171C")]
    [InlineData("TextControlBackgroundFocused", "#14171C")]
    [InlineData("ComboBoxDropDownBackground", "#252D37")]
    [InlineData("ComboBoxDropDownBorderBrush", "#35404D")]
    public void WhenTheAppDrawsInDarkGraphite_ShouldUseItsColour(string key, string colour) =>
        Assert.Equal(Color.Parse(colour), ColourOf(key, ThemeVariant.Dark));

    // Espresso keeps Fluent's dark list colours, as before Graphite came; its text fields lie on its surface, as in the
    // other dark themes, where Fluent darkened them to black.
    [AvaloniaTheory]
    [InlineData("SurfaceBrush", "#201E1C")]
    [InlineData("ChromeBrush", "#272421")]
    [InlineData("DialogBrush", "#201E1C")]
    [InlineData("TextBrush", "#EAE5DE")]
    [InlineData("AccentBrush", "#E88B5E")]
    [InlineData("AccentStrongBrush", "#E07A3F")]
    [InlineData("TextControlBackground", "#201E1C")]
    [InlineData("TextControlBackgroundPointerOver", "#201E1C")]
    [InlineData("TextControlBackgroundFocused", "#201E1C")]
    [InlineData("ComboBoxDropDownBackground", "#2B2B2B")]
    public void WhenTheAppDrawsInDarkEspresso_ShouldKeepItsWarmColours(string key, string colour) =>
        Assert.Equal(Color.Parse(colour), ColourOf(key, AppThemes.DarkEspresso));

    // Obsidian, the darkest: near-black neutral greys under Graphite's orange, its text fields on its surface as in Graphite.
    [AvaloniaTheory]
    [InlineData("SurfaceBrush", "#0C0C0E")]
    [InlineData("ChromeBrush", "#0E0E10")]
    [InlineData("DialogBrush", "#121214")]
    [InlineData("ControlBrush", "#17171A")]
    [InlineData("DividerBrush", "#232326")]
    [InlineData("TextBrush", "#E6E6E8")]
    [InlineData("MutedBrush", "#919196")]
    [InlineData("AccentBrush", "#E38D5D")]
    [InlineData("AccentStrongBrush", "#E07A3F")]
    [InlineData("TextControlBackground", "#0C0C0E")]
    [InlineData("TextControlBackgroundFocused", "#0C0C0E")]
    [InlineData("ComboBoxDropDownBackground", "#17171A")]
    public void WhenTheAppDrawsInObsidian_ShouldUseItsColour(string key, string colour) =>
        Assert.Equal(Color.Parse(colour), ColourOf(key, AppThemes.DarkObsidian));

    // A theme drawn from Dark gives its settings window the main window's shades: its background and its cards the bars',
    // the chosen section and a drop-down the panes'.
    [AvaloniaTheory]
    [InlineData("DarkEspresso", "SettingsSurface", "ChromeBrush")]
    [InlineData("DarkEspresso", "SettingsCard", "ChromeBrush")]
    [InlineData("DarkEspresso", "SettingsSelected", "SurfaceBrush")]
    [InlineData("DarkEspresso", "ComboBoxBackground", "SurfaceBrush")]
    [InlineData("DarkObsidian", "SettingsSurface", "ChromeBrush")]
    [InlineData("DarkObsidian", "SettingsCard", "ChromeBrush")]
    [InlineData("DarkObsidian", "SettingsSelected", "SurfaceBrush")]
    [InlineData("DarkObsidian", "ComboBoxBackground", "SurfaceBrush")]
    [InlineData("DarkVantablack", "SettingsSurface", "ChromeBrush")]
    [InlineData("DarkVantablack", "SettingsCard", "ChromeBrush")]
    [InlineData("DarkVantablack", "SettingsSelected", "SurfaceBrush")]
    [InlineData("DarkVantablack", "ComboBoxBackground", "SurfaceBrush")]
    public void WhenAThemeDrawnFromDarkDrawsTheSettings_ShouldUseTheMainWindowsShades(string theme, string settingsKey, string mainKey) =>
        Assert.Equal(ColourOf(mainKey, Variant(theme)), ColourOf(settingsKey, Variant(theme)));

    // A menu opens on the colour of the sidebar beside it in a dark theme, Trag's own menus and Fluent's alike.
    [AvaloniaTheory]
    [InlineData("Dark", "actions")]
    [InlineData("Dark", "")]
    [InlineData("DarkEspresso", "actions")]
    [InlineData("DarkEspresso", "")]
    [InlineData("DarkObsidian", "actions")]
    [InlineData("DarkObsidian", "")]
    [InlineData("DarkVantablack", "actions")]
    [InlineData("DarkVantablack", "")]
    public void WhenAMenuOpensInADarkTheme_ShouldDrawOnTheSidebarsColour(string theme, string presenterClass)
    {
        var item = new MenuItem { Header = "Open" };
        var menu = new MenuFlyout { Items = { item } };
        if (presenterClass.Length != 0) menu.FlyoutPresenterClasses.Add(presenterClass);
        var button = new Button { Content = "Menu", Flyout = menu };
        var window = new Window { RequestedThemeVariant = Variant(theme), Content = button };
        window.Show(); AppHarness.Layout(window);
        menu.ShowAt(button); AppHarness.Layout(window);

        var presenter = item.GetVisualAncestors().OfType<MenuFlyoutPresenter>().Single();
        Assert.Equal(ColourOf("ChromeBrush", Variant(theme)), ((ISolidColorBrush)presenter.Background!).Color);
        menu.Hide();
        window.Close();
    }

    // A hovered section in the settings stands apart both from the list and from the chosen section.
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("DarkEspresso")]
    [InlineData("DarkObsidian")]
    [InlineData("DarkVantablack")]
    public void WhenASettingsSectionIsHovered_ShouldStandApartFromTheListAndTheChosenOne(string theme)
    {
        var hover = ColourOf("SettingsHover", Variant(theme));
        Assert.True(Distance(hover, ColourOf("SettingsCard", Variant(theme))) >= 3, "list");
        Assert.True(Distance(hover, ColourOf("SettingsSelected", Variant(theme))) >= 3, "chosen");
    }

    // Vantablack: black bars and panes with neutral grey lines and selection, under Graphite's orange; dialogs a step
    // above the black.
    [AvaloniaTheory]
    [InlineData("SurfaceBrush", "#000000")]
    [InlineData("ChromeBrush", "#000000")]
    [InlineData("DialogBrush", "#0A0A0A")]
    [InlineData("ControlBrush", "#000000")]
    [InlineData("DividerBrush", "#1E1E1E")]
    [InlineData("SelectedBrush", "#181818")]
    [InlineData("TextBrush", "#E6E6E6")]
    [InlineData("MutedBrush", "#8A8A8A")]
    [InlineData("AccentBrush", "#E38D5D")]
    [InlineData("AccentStrongBrush", "#E07A3F")]
    [InlineData("TextControlBackground", "#000000")]
    [InlineData("TextControlBackgroundFocused", "#000000")]
    [InlineData("ComboBoxDropDownBackground", "#111111")]
    public void WhenTheAppDrawsInVantablack_ShouldUseItsColour(string key, string colour) =>
        Assert.Equal(Color.Parse(colour), ColourOf(key, AppThemes.DarkVantablack));

    [AvaloniaTheory]
    [InlineData("avares://Trag/Themes/Palette.axaml", "DarkEspresso")]
    [InlineData("avares://Trag/Themes/FluentOverrides.axaml", "DarkEspresso")]
    [InlineData("avares://Trag/Themes/Palette.axaml", "DarkObsidian")]
    [InlineData("avares://Trag/Themes/FluentOverrides.axaml", "DarkObsidian")]
    [InlineData("avares://Trag/Themes/Palette.axaml", "DarkVantablack")]
    [InlineData("avares://Trag/Themes/FluentOverrides.axaml", "DarkVantablack")]
    public void WhenDarkGraphiteNamesABrush_ShouldEveryThemeDrawnFromItNameItToo(string source, string theme)
    {
        var dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(source));
        var graphite = (ResourceDictionary)dictionary.ThemeDictionaries[ThemeVariant.Dark];
        var drawnFromIt = (ResourceDictionary)dictionary.ThemeDictionaries[Variant(theme)];
        Assert.Empty(graphite.Keys.Except(drawnFromIt.Keys));
    }

    [AvaloniaTheory]
    [InlineData("DarkEspresso")]
    [InlineData("DarkObsidian")]
    [InlineData("DarkVantablack")]
    public void WhenFluentDrawsInAThemeDrawnFromDark_ShouldDrawAsInDark(string theme)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource("SystemControlBackgroundAltHighBrush", ThemeVariant.Dark, out var dark));
        Assert.True(Avalonia.Application.Current!.TryGetResource("SystemControlBackgroundAltHighBrush", Variant(theme), out var drawnFromIt));
        Assert.Same(dark, drawnFromIt);
    }

    // The dark themes accent in the orange of the graph's first lane, Fluent's own controls included.
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("DarkEspresso")]
    [InlineData("DarkObsidian")]
    [InlineData("DarkVantablack")]
    public void WhenFluentDrawsAnAccentInADarkTheme_ShouldUseTheGraphsOrange(string theme) =>
        Assert.Equal(Color.Parse("#E07A3F"), ColourOf("SystemControlHighlightAccentBrush", Variant(theme)));

    // Text and icons take the softer accent, which a thin line needs to look as bright as a filled surface; filled surfaces
    // keep the graph's orange. White has one accent for both.
    [AvaloniaFact]
    public void WhenAnAccentButtonIsDrawnInADarkTheme_ShouldFillWithTheGraphsOrange()
    {
        var button = new Button { Classes = { "accent" }, Content = "Confirm" };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = button };
        window.Show(); AppHarness.Layout(window);

        Assert.Equal(Color.Parse("#E07A3F"), ((ISolidColorBrush)button.Background!).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void WhenTheAppDrawsInWhite_ShouldFillWithItsOneAccent() => Assert.Equal(ColourOf("AccentBrush", ThemeVariant.Light), ColourOf("AccentStrongBrush", ThemeVariant.Light));

    // A selected row in a dark theme is the softer accent and lets more of the surface through than Fluent's: more under
    // the pointer, most while pressed.
    [AvaloniaTheory]
    [InlineData("Dark", "SystemControlHighlightListAccentLowBrush", 0.55)]
    [InlineData("Dark", "SystemControlHighlightListAccentMediumBrush", 0.65)]
    [InlineData("Dark", "SystemControlHighlightListAccentHighBrush", 0.75)]
    [InlineData("DarkEspresso", "SystemControlHighlightListAccentLowBrush", 0.55)]
    [InlineData("DarkEspresso", "SystemControlHighlightListAccentMediumBrush", 0.65)]
    [InlineData("DarkEspresso", "SystemControlHighlightListAccentHighBrush", 0.75)]
    [InlineData("DarkObsidian", "SystemControlHighlightListAccentLowBrush", 0.55)]
    [InlineData("DarkObsidian", "SystemControlHighlightListAccentMediumBrush", 0.65)]
    [InlineData("DarkObsidian", "SystemControlHighlightListAccentHighBrush", 0.75)]
    [InlineData("DarkVantablack", "SystemControlHighlightListAccentLowBrush", 0.55)]
    [InlineData("DarkVantablack", "SystemControlHighlightListAccentMediumBrush", 0.65)]
    [InlineData("DarkVantablack", "SystemControlHighlightListAccentHighBrush", 0.75)]
    public void WhenARowIsSelectedInADarkTheme_ShouldTintItSoftly(string theme, string key, double opacity)
    {
        var variant = Variant(theme);
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant, out var brush));
        Assert.Equal((ColourOf("AccentBrush", variant), opacity), (((ISolidColorBrush)brush!).Color, ((ISolidColorBrush)brush).Opacity));
    }

    [AvaloniaFact]
    public void WhenARowIsSelectedInGraphite_ShouldDrawItsSoftTint()
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two" }, SelectedIndex = 0 };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = list };
        window.Show(); AppHarness.Layout(window);

        var row = list.GetRealizedContainers().OfType<ListBoxItem>().First().GetVisualDescendants().OfType<ContentPresenter>().First(presenter => presenter.Name == "PART_ContentPresenter");
        Assert.Equal(0.55, ((ISolidColorBrush)row.Background!).Opacity);
        window.Close();
    }

    // White and Espresso draw their dialogs on the surface; Graphite draws them a step above it.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("DarkEspresso")]
    public void WhenADialogIsDrawnInLightOrDarkEspresso_ShouldStayOnTheSurface(string theme) =>
        Assert.Equal(ColourOf("SurfaceBrush", Variant(theme)), ColourOf("DialogBrush", Variant(theme)));

    [AvaloniaFact]
    public void WhenADialogShowsInDarkGraphite_ShouldDrawItsCardInTheDialogColour()
    {
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = new DialogHost { IsOpen = true } };
        window.Show(); AppHarness.Layout(window);

        Assert.Equal(Color.Parse("#1E242C"), ((ISolidColorBrush)window.Descendant<Border>("Card")!.Background!).Color);
        window.Close();
    }

    // Code and diffs take the dark syntax colours in every dark theme; a control outside a window has no theme yet.
    [Fact]
    public void WhenAThemeIsDarkOrDrawnFromDark_ShouldBeDark()
    {
        Assert.True(AppThemes.IsDark(ThemeVariant.Dark)); Assert.True(AppThemes.IsDark(AppThemes.DarkEspresso)); Assert.True(AppThemes.IsDark(AppThemes.DarkObsidian));
        Assert.True(AppThemes.IsDark(AppThemes.DarkVantablack));
        Assert.False(AppThemes.IsDark(ThemeVariant.Light)); Assert.False(AppThemes.IsDark(ThemeVariant.Default)); Assert.False(AppThemes.IsDark(null));
    }

    private static ThemeVariant Variant(string theme) => theme switch
    {
        "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, "DarkEspresso" => AppThemes.DarkEspresso, "DarkObsidian" => AppThemes.DarkObsidian,
        _ => AppThemes.DarkVantablack
    };

    private static int Distance(Color one, Color other) => Math.Max(Math.Abs(one.R - other.R), Math.Max(Math.Abs(one.G - other.G), Math.Abs(one.B - other.B)));

    private static Color ColourOf(string key, ThemeVariant theme)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, theme, out var brush), key);
        return ((ISolidColorBrush)brush!).Color;
    }
}
