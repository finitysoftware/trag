using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Trag.Desktop.UnitTests.Themes;

// A drop-down's chosen item sits in the middle of its box, where Fluent's padding leaves it a pixel high.
public sealed class ComboBoxTests
{
    [AvaloniaFact]
    public void WhenADropDownShowsItsChoice_ShouldCentreItVertically()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Obsidian" }, SelectedIndex = 0, MinWidth = 170 };
        var window = new Window { Content = new StackPanel { Children = { combo } } };
        window.Show(); AppHarness.Layout(window);

        var text = combo.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Obsidian");
        var top = text.TranslatePoint(new Point(0, 0), combo)!.Value.Y;
        Assert.Equal(combo.Bounds.Height / 2, top + text.Bounds.Height / 2);
        window.Close();
    }
}
