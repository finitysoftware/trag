using Avalonia.Styling;

namespace Trag.Desktop.Themes;

// The theme variants beyond Avalonia's Light and Dark, which is Graphite. Espresso, Obsidian and Vantablack are drawn from
// Dark: what they give no colour of their own, Fluent's controls included, they take from Dark.
public static class AppThemes
{
    public static ThemeVariant DarkEspresso { get; } = new("DarkEspresso", ThemeVariant.Dark);
    public static ThemeVariant DarkObsidian { get; } = new("DarkObsidian", ThemeVariant.Dark);
    public static ThemeVariant DarkVantablack { get; } = new("DarkVantablack", ThemeVariant.Dark);

    // Code and diffs take the dark syntax colours in Dark and in every theme drawn from it; a control outside a window has
    // no theme yet.
    public static bool IsDark(ThemeVariant? theme) => theme == ThemeVariant.Dark || theme?.InheritVariant == ThemeVariant.Dark;
}
