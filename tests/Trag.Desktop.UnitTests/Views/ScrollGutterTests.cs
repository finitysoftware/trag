using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Views.Pickers;

namespace Trag.Desktop.UnitTests.Views;

// A scroll bar is drawn over its viewer's content, so a view whose rows, fields or cards reach the right edge scrolls
// across a gutter as wide as the bar: the bar then lies beside them instead of over their buttons.
public sealed class ScrollGutterTests
{
    [AvaloniaTheory]
    [InlineData("branch picker")]
    [InlineData("stash picker")]
    [InlineData("tag picker")]
    [InlineData("publish dialog")]
    [InlineData("merge dialog")]
    [InlineData("settings")]
    public void WhenAViewScrolls_ShouldKeepItsContentOutOfTheScrollBarsWay(string view)
    {
        var window = Shown(view);
        // The view's own viewer, not one inside a template such as a text box's.
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(scroll => scroll.TemplatedParent is null);
        var content = Assert.IsAssignableFrom<Control>(scroll.Content);
        var bar = Assert.IsType<double>(window.FindResource("ScrollBarSize"));

        var contentEnd = content.TranslatePoint(new Point(content.Bounds.Width, 0), scroll)!.Value.X;

        Assert.True(contentEnd <= scroll.Bounds.Width - bar + 0.5, $"{view}: the content ends at {contentEnd} of {scroll.Bounds.Width}");
        window.Close();
    }

    private static Window Shown(string view)
    {
        var window = view switch
        {
            "branch picker" => new Window { Content = new BranchPicker() },
            "stash picker" => new Window { Content = new StashPicker() },
            "tag picker" => new Window { Content = new TagPicker() },
            "publish dialog" => new Window { Content = new PublishDialog { IsOpen = true } },
            "merge dialog" => new Window { Content = new MergeBranchesDialog { IsOpen = true } },
            _ => new SettingsWindow(),
        };
        window.Show(); AppHarness.Layout(window);
        return window;
    }
}
