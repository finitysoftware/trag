using Avalonia.Styling;
using Trag.Application.Live;
using Trag.Application.Ports.Storage;
using Trag.Desktop.Themes;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Composition;

// Follows the layout's theme with the application's requested theme variant.
public static class ThemeController
{
    public static ThemeVariant VariantFor(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Light => ThemeVariant.Light, ThemeChoice.DarkEspresso => AppThemes.DarkEspresso, ThemeChoice.DarkGraphite => ThemeVariant.Dark,
        ThemeChoice.DarkObsidian => AppThemes.DarkObsidian, ThemeChoice.DarkVantablack => AppThemes.DarkVantablack, _ => ThemeVariant.Default
    };

    // The saved theme before the window first draws. The store reads off the UI thread, so waiting here cannot deadlock; a
    // layout that cannot be read leaves the default, and the layout's own read after the window opens says so.
    public static void ShowSaved(Avalonia.Application application, ILayoutStore store)
    {
        try { application.RequestedThemeVariant = VariantFor((ThemeChoice)store.LoadLayoutAsync(CancellationToken.None).GetAwaiter().GetResult().Theme); }
        catch (Exception) { }
    }

    public static void Follow(Avalonia.Application application, LayoutPreferences layout) => layout.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(LayoutPreferences.ThemeIndex)) application.RequestedThemeVariant = VariantFor(layout.Theme);
    };
}
