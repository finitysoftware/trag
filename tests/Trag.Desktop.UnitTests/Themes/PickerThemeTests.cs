using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Desktop.Themes;
using Trag.Desktop.Views;
using Trag.Desktop.Views.Pickers;

namespace Trag.Desktop.UnitTests.Themes;

// The sidebar's pickers (branches, stashes, tags, remote, upstream) open on the sidebar's colour in a dark theme and on
// the surface in Light, and their search box takes the picker's colour, at rest and in focus, instead of a darker field
// on it.
public sealed class PickerThemeTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Espresso")]
    [InlineData("Graphite")]
    [InlineData("Obsidian")]
    [InlineData("Vantablack")]
    public async Task WhenTheBranchPickerOpens_ShouldDrawLikeTheSidebarWithItsSearchBoxInThePickersColour(string name)
    {
        var theme = name switch
        {
            "Light" => ThemeVariant.Light, "Espresso" => AppThemes.DarkEspresso, "Graphite" => ThemeVariant.Dark,
            "Obsidian" => AppThemes.DarkObsidian, _ => AppThemes.DarkVantablack,
        };
        await using var model = new AppHarness().Shell();
        var window = new MainWindow { DataContext = model, RequestedThemeVariant = theme }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Branches.List.Completion; AppHarness.Layout(window);

        var button = window.Descendant<Button>("BranchButton")!;
        var flyout = (Flyout)button.Flyout!;
        flyout.ShowAt(button); AppHarness.Layout(window);
        var picker = (BranchPicker)flyout.Content!;

        var presenter = picker.GetVisualAncestors().OfType<FlyoutPresenter>().Single();
        Assert.True(window.TryFindResource(name == "Light" ? "SurfaceBrush" : "ChromeBrush", theme, out var background));
        Assert.Equal(((ISolidColorBrush)background!).Color, ((ISolidColorBrush)presenter.Background!).Color);
        var search = picker.Descendant<TextBox>("BranchSearchEditor")!;
        var field = search.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
        Assert.Equal(((ISolidColorBrush)background).Color, ((ISolidColorBrush)field.Background!).Color);
        search.Focus(); AppHarness.Layout(window);
        Assert.Equal(((ISolidColorBrush)background).Color, ((ISolidColorBrush)field.GetBaseValue(Border.BackgroundProperty).GetValueOrDefault()!).Color);
        flyout.Hide();
        window.Close();
    }
}
