using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;

namespace Trag.Desktop.UnitTests.Views;

// The right end of the toolbar: a terminal in the repository's folder and More, small buttons with the icon beside the
// caption. More holds the repository's less frequent actions above a separator and Settings under it; without an open
// repository it holds Settings alone.
public sealed class ShellActionsTests
{
    [AvaloniaFact]
    public async Task WhenARepositoryIsOpen_ShouldOfferATerminalAndMoreWithTheRepositorysActionsAboveSettings()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var terminal = window.Descendant<Button>("TerminalButton")!;
        var more = window.Descendant<Button>("MoreButton")!;
        Assert.False(terminal.IsEffectivelyVisible); Assert.True(more.IsEffectivelyVisible);
        Assert.Equal(["Settings"], Shown(window, more));

        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        Assert.True(terminal.IsEffectivelyVisible);
        Assert.Equal([("Terminal", "terminal"), ("More", "more")], new[] { terminal, more }.Select(Face));
        Assert.All([terminal, more], IconBesideCaption);
        Assert.Null(window.Descendant<Button>("SettingsButton")); Assert.Null(window.Descendant<ComboBox>("ThemeSelector"));
        Assert.Equal(["Remotes", "Repository details", "—", "Settings"], Shown(window, more));
        // A menu item is bound while its menu is open.
        more.Flyout!.ShowAt(more); AppHarness.Layout(window);
        var items = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["cloud", "info", "settings"], items.Select(item => Assert.IsType<LineIcon>(item.Icon).Kind));
        Assert.Same(model.RemoteSettings.OpenCommand, items[0].Command);
        Assert.Same(model.RepositoryDetails.OpenCommand, items[1].Command);
        Assert.Same(model.Catalog.RequestSettingsCommand, items[2].Command);
        Assert.Same(model.OpenTerminalCommand, terminal.Command);
        window.Close();
    }

    // The menu's entries as it shows them, a separator as a dash.
    private static string[] Shown(Window window, Button more)
    {
        more.Flyout!.ShowAt(more); AppHarness.Layout(window);
        var shown = ((MenuFlyout)more.Flyout).Items.OfType<Control>().Where(entry => entry.IsVisible)
            .Select(entry => entry is MenuItem item ? (string)item.Header! : "—").ToArray();
        more.Flyout.Hide(); AppHarness.Layout(window);
        return shown;
    }

    private static (string?, string) Face(Button button) =>
        (button.GetVisualDescendants().OfType<TextBlock>().Single().Text, button.GetVisualDescendants().OfType<LineIcon>().Single().Kind);

    // No tile around the icon, and the icon on the caption's line, before it. The terminal's frame is a full box, which
    // centred on the caption's line looks low beside its capitals, so it sits 1 px higher; More's dots sit on the line.
    private static void IconBesideCaption(Button button)
    {
        Assert.DoesNotContain(button.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("iconTile"));
        var icon = button.GetVisualDescendants().OfType<LineIcon>().Single();
        var caption = button.GetVisualDescendants().OfType<TextBlock>().Single();
        var iconAt = icon.TranslatePoint(new Point(icon.Bounds.Width, icon.Bounds.Height / 2), button)!.Value;
        var captionAt = caption.TranslatePoint(new Point(0, caption.Bounds.Height / 2), button)!.Value;
        Assert.True(iconAt.X <= captionAt.X, $"{button.Name}: the icon ends at {iconAt.X}, the caption starts at {captionAt.X}");
        Assert.Equal(captionAt.Y - (icon.Kind == "terminal" ? 1 : 0), iconAt.Y, 2);
    }
}
