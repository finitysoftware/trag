using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Trag.Desktop.UnitTests.Controls;

// Every scroll bar of the app keeps Fluent's thin line while the pointer is away; under the pointer it expands into the
// app's own look, an accent thumb without the arrow buttons of a classic scroll bar.
public sealed class ScrollBarTests
{
    [AvaloniaFact]
    public async Task WhenThePointerExpandsAScrollBar_ShouldDrawItsThumbInTheAccentWithoutArrows()
    {
        var scroll = new ScrollViewer { Height = 300, Content = new Border { Height = 3000 } };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        window.Show(); AppHarness.Layout(window);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == Orientation.Vertical);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var accent = window.FindResource(window.ActualThemeVariant, "AccentBrush");
        Assert.NotSame(accent, thumb.GetBaseValue(TemplatedControl.BackgroundProperty).GetValueOrDefault());

        await ExpandedAsync(window, bar);

        // The base value: the brush the styles give, which the thumb's transition moves to.
        Assert.Same(accent, thumb.GetBaseValue(TemplatedControl.BackgroundProperty).GetValueOrDefault());
        var arrows = bar.GetVisualDescendants().OfType<RepeatButton>().Where(button => button.Name is "PART_LineUpButton" or "PART_LineDownButton").ToArray();
        Assert.Equal(2, arrows.Length);
        Assert.All(arrows, arrow => Assert.False(arrow.IsEffectivelyVisible));
        window.Close();
    }

    // Expanded, the thumb is drawn at its full width, exactly over the track, across the bar either way.
    [AvaloniaTheory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public async Task WhenAScrollBarIsExpanded_ShouldDrawItsThumbAtFullWidthExactlyOverTheTrack(Orientation orientation)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Width = 3000, Height = 3000 } };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        window.Show(); AppHarness.Layout(window);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == orientation);
        await ExpandedAsync(window, bar);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var under = bar.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "TrackRect");

        var thumbFrom = thumb.TranslatePoint(default, bar)!.Value;
        var thumbTo = thumb.TranslatePoint(new Point(thumb.Bounds.Width, thumb.Bounds.Height), bar)!.Value;
        var underFrom = under.TranslatePoint(default, bar)!.Value;
        var underTo = under.TranslatePoint(new Point(under.Bounds.Width, under.Bounds.Height), bar)!.Value;

        var vertical = orientation == Orientation.Vertical;
        Assert.Equal(8, vertical ? thumbTo.X - thumbFrom.X : thumbTo.Y - thumbFrom.Y, 1);
        Assert.Equal(vertical ? (underFrom.X, underTo.X) : (underFrom.Y, underTo.Y), vertical ? (thumbFrom.X, thumbTo.X) : (thumbFrom.Y, thumbTo.Y));
        window.Close();
    }

    // Collapsed, the thumb keeps the width and shape it has expanded and is only narrowed into Fluent's thin line, 2 px
    // wide and 2 px in from the bar's edge; so expanding and collapsing only narrow it, it never widens into Fluent's
    // 16 px thumb first.
    [AvaloniaTheory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void WhenAScrollBarIsCollapsed_ShouldDrawItsThinLineByNarrowingTheExpandedThumb(Orientation orientation)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Width = 3000, Height = 3000 } };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        window.Show(); AppHarness.Layout(window);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == orientation);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        Assert.False(bar.IsExpanded);

        var near = thumb.TranslatePoint(default, bar)!.Value;
        var far = thumb.TranslatePoint(new Point(thumb.Bounds.Width, thumb.Bounds.Height), bar)!.Value;

        var vertical = orientation == Orientation.Vertical;
        Assert.Equal(8, vertical ? thumb.Bounds.Width : thumb.Bounds.Height, 1);
        Assert.Equal(new CornerRadius(4), thumb.CornerRadius);
        var across = vertical ? bar.Bounds.Width : bar.Bounds.Height;
        Assert.Equal(across - 4, vertical ? near.X : near.Y, 1);
        Assert.Equal(across - 2, vertical ? far.X : far.Y, 1);
        window.Close();
    }

    // Fluent paints the thumb under the pointer and while dragged grey again; it stays in the accent.
    [AvaloniaFact]
    public async Task WhenThePointerIsOnTheThumbOrDragsIt_ShouldKeepItInTheAccent()
    {
        var scroll = new ScrollViewer { Height = 300, Content = new Border { Height = 3000 } };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        window.Show(); AppHarness.Layout(window);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == Orientation.Vertical);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var accent = window.FindResource(window.ActualThemeVariant, "AccentBrush");
        await ExpandedAsync(window, bar);
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

        window.MouseMove(point); AppHarness.Layout(window);
        Assert.Same(accent, Painted(thumb));
        window.MouseDown(point, MouseButton.Left); AppHarness.Layout(window);
        Assert.Same(accent, Painted(thumb));
        window.MouseUp(point, MouseButton.Left);
        window.Close();
    }

    // The brush the thumb's own border is given: Fluent sets it there, under the pointer and while pressed.
    private static object? Painted(Thumb thumb) =>
        thumb.GetVisualDescendants().OfType<Border>().First().GetBaseValue(Border.BackgroundProperty).GetValueOrDefault();

    // The pointer resting on the bar, as long as Fluent's delay takes to expand it, and its 0.1 s animation.
    private static async Task ExpandedAsync(Window window, ScrollBar bar)
    {
        window.MouseMove(bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value);
        for (var wait = 0; wait < 30 && !bar.IsExpanded; wait++) { await Task.Delay(100); AppHarness.Layout(window); }
        Assert.True(bar.IsExpanded);
        await Task.Delay(300); AppHarness.Layout(window);
    }
}
