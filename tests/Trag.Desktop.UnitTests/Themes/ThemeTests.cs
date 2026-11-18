using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Desktop.Composition;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Themes;
using Trag.Desktop.UnitTests.Dialogs.Settings;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Themes;

// A theme chosen while windows are open redraws them as they stand.
public sealed class ThemeTests
{
    // The settings window gives Windows its background and text colours for the title bar Windows draws, so they are
    // the theme's as soon as it changes. Settings keep the keyboard while they are open and give it back to the dialog
    // when they close.
    [AvaloniaFact]
    public async Task WhenTheThemeChangesWhileADialogAndSettingsAreOpen_ShouldRestyleBothWithoutLosingFocus()
    {
        var application = Avalonia.Application.Current!;
        var h = new AppHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        try
        {
            ThemeController.Follow(application, model.Layout);
            await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
            model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
            Assert.Equal("StashMessageEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
            var card = window.Descendant<TextBox>("StashMessageEditor")!.FindAncestorOfType<DialogHost>()!.Descendant<Border>("Card")!;
            model.Catalog.RequestSettingsCommand.Execute(null);
            await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings");
            var settings = window.SettingsHost.ActiveSettings!; AppHarness.Layout(settings);
            SettingsHarness.Choose(settings, "AppearanceSection");
            var selector = settings.Descendant<ComboBox>("ThemeSelector")!;
            selector.Focus(); AppHarness.Layout(settings);

            foreach (var (index, theme) in new[] { (5, AppThemes.DarkVantablack), (1, ThemeVariant.Light) })
            {
                selector.SelectedIndex = index; AppHarness.Layout(settings); AppHarness.Layout(window);

                Assert.Equal(ColourOf("DialogBrush", theme), ((ISolidColorBrush)card.Background!).Color);
                Assert.Equal(ColourOf("SettingsSurface", theme), ((ISolidColorBrush)settings.Background!).Color);
                Assert.Equal(ColourOf("TextBrush", theme), ((ISolidColorBrush)settings.Foreground!).Color);
                Assert.Equal("ThemeSelector", (settings.FocusManager!.GetFocusedElement() as Control)?.Name);
            }
            settings.Close(); AppHarness.Layout(window);
            await AppHarness.Until(() => (window.FocusManager!.GetFocusedElement() as Control)?.Name == "StashMessageEditor", "the focus back in the stash dialog");
        }
        finally { application.RequestedThemeVariant = ThemeVariant.Default; await model.Layout.SaveCompletion; window.Close(); }
    }

    private static Color ColourOf(string key, ThemeVariant theme)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, theme, out var brush), key);
        return ((ISolidColorBrush)brush!).Color;
    }
}
