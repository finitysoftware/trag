using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trag.Application.Diff;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Desktop.UnitTests.Controls.Diff;

public sealed class DiffViewTests
{
    [AvaloniaFact]
    public async Task WhenAHunkStartsAtTheMillionthLine_ShouldWidenTheGutterForBothLineNumbers()
    {
        var document = await new BackgroundDiffBuilder().BuildAsync(new([], [new(' ', " context", 1_000_001, 1_000_002)], DiffContentKind.Text, ""), "file.txt", default);
        var view = new DiffView { Document = document };
        Assert.Equal(7, document.NumberDigits);
        Assert.True(view.GutterWidth >= 114);
        view.Mode = 1; Assert.True(view.GutterWidth >= 57);
    }
    [AvaloniaFact]
    public async Task WhenLargeDocumentsAreReplaced_ShouldLetTheRecycledRowsReleaseThem()
    {
        var view = new DiffView();
        var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show();
        var references = new List<WeakReference>();
        for (var i = 0; i < 4; i++) references.Add(await DisplayDocument(view, window));
        view.Document = DiffPresentation.Empty;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        // Let the final async helper return before checking its former local variables.
        await Task.Yield();
        // Retention assertion only, never used by production or resident-memory benchmarks.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        window.Close();
    }
    private static async Task<WeakReference> DisplayDocument(DiffView view, Window window)
    {
        var lines = Enumerable.Range(0, 10_000).Select(i => new PatchLine('+', "+var value = " + i, null, i)).ToArray();
        var document = await new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "file.cs", default);
        view.Document = document;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        view.Descendant<ListBox>("Rows")!.ScrollIntoView(500);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return new(document);
    }
    [AvaloniaFact]
    public async Task WhenTheUserSwitchesModesOnALargeDiff_ShouldKeepTheSelectionPlaceAndFocusWithBoundedRowsUntilCleared()
    {
        var lines = new List<PatchLine>();
        for (var i = 1; i <= 10_000; i++) { lines.Add(new('-', "-old " + i, i)); lines.Add(new('+', "+new " + i, null, i)); }
        var document = await new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "sample.cs", default);
        var view = new DiffView { Document = document };
        var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var list = view.Descendant<ListBox>("Rows")!;
        list.SelectedIndex = 801; list.ScrollIntoView(list.SelectedItem!); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var top = view.TopSource;
        view.Descendant<ComboBox>("DiffModeSelector")!.SelectedIndex = 1; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.Equal(801, view.SelectedSource);
        Assert.Equal(document.ToSplit(801), list.SelectedIndex);
        Assert.InRange(Math.Abs(view.TopSource - top), 0, 2);
        Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.InRange(list.GetRealizedContainers().Count(), 1, 100);
        view.Mode = 0; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(801, list.SelectedIndex);
        Assert.True(list.Focus());
        view.Mode = 1; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.True(list.IsKeyboardFocusWithin);
        var selection = list.SelectedIndex;
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(selection + 1, list.SelectedIndex);
        view.Document = DiffPresentation.Empty; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(list.GetRealizedContainers()); Assert.Equal(-1, view.SelectedSource);
        window.Close();
    }

    // Another file starts at its first column; the same file read again keeps the place the user scrolled to.
    [AvaloniaFact]
    public async Task WhenTheDocumentChangesToAnotherSourceWhileScrolledRight_ShouldResetTheHorizontalOffset()
    {
        async Task<DiffPresentation> LongLineAsync(string path) => await new BackgroundDiffBuilder()
            .BuildAsync(new([], [new('+', "+" + new string('x', 2_000), null, 1)], DiffContentKind.Text, ""), path, default) with { Source = new(path, "working tree") };
        var view = new DiffView { Document = await LongLineAsync("a.txt") };
        var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        view.HorizontalOffset = 500;

        view.Document = await LongLineAsync("a.txt");
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var kept = view.HorizontalOffset;
        view.Document = await LongLineAsync("b.txt");
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();

        Assert.Equal((500, 0), (kept, view.HorizontalOffset));
        window.Close();
    }

    // Each character of the line is distinct, so the first one drawn tells how many columns lie before it.
    [AvaloniaFact]
    public async Task WhenALineOfWideCharactersIsScrolledRight_ShouldDrawItsTextAtTheColumnsBeforeIt()
    {
        var text = "+" + string.Concat(Enumerable.Range(0, 300).Select(i => (char)('\u4E00' + i)));
        var (view, window) = await ShowAsync([new('+', text, null, 1)]);
        var row = view.GetVisualDescendants().OfType<DiffRowControl>().Single();
        var origin = DrawnText(row).X;

        view.HorizontalOffset = 1_200;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var (shown, x) = DrawnText(row);

        var columnsBefore = 1 + 2 * (shown[0] - '\u4E00');
        Assert.Equal(origin + columnsBefore * DiffRowControl.CellWidth - 1_200, x);
        window.Close();
    }

    // In a diff whose lines end both ways a line whose only change is its ending shows the mark that tells them apart.
    [AvaloniaFact]
    public async Task WhenRowsMixCrlfAndLf_ShouldDrawTheCrlfMark()
    {
        var (view, window) = await ShowAsync([new('h', "@@"), new('-', "-same", 1), new('+', "+same", null, 1) { HasCarriageReturn = true }]);
        var drawn = view.GetVisualDescendants().OfType<DiffRowControl>().Select(row => DrawnText(row).Text);
        Assert.Equal(["-same", "+same" + FileDiffText.CrlfMark], drawn);
        window.Close();
    }

    // A diff narrower than its header, as in History at 880 px, puts the counts of lines that do not fit beside the mode
    // selector on another line instead of cutting their text at its edge; the rows, and the hunk buttons over them, start
    // below the taller header.
    [AvaloniaFact]
    public async Task WhenTheDiffIsNarrowerThanItsHeader_ShouldKeepTheCountsOfLinesInside()
    {
        var (view, window) = await ShowAsync([new('h', "@@"), new('-', "-old", 1), new('+', "+new", null, 1)]);
        window.Width = 400; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var removed = view.Descendant<TextBlock>("RemovedLinesLabel")!;

        var corner = removed.TranslatePoint(new(removed.TextLayout.Width, removed.Bounds.Height), view)!.Value;

        Assert.True(removed.IsEffectivelyVisible);
        Assert.True(corner.X <= view.Bounds.Width);
        Assert.True(view.Descendant<Panel>("RowsArea")!.TranslatePoint(default, view)!.Value.Y >= corner.Y);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheDiffIsWideEnoughForItsHeader_ShouldKeepTheChangeBadgeItsOwnHeight()
    {
        var (view, window) = await ShowAsync([new('h', "@@"), new('-', "-old", 1), new('+', "+new", null, 1)]);
        var badge = (Control)view.Descendant<TextBlock>("ChangeLabel")!.GetVisualParent()!;

        Assert.True(badge.IsEffectivelyVisible);
        Assert.Equal(badge.DesiredSize.Height, badge.Bounds.Height);
        window.Close();
    }

    // A header that fits looks as it did before it could wrap: the mode selector at the left, the badge and the counts of
    // lines at the right edge, all on one line.
    [AvaloniaFact]
    public async Task WhenTheDiffIsWideEnoughForItsHeader_ShouldShowTheCountsOfLinesAtItsRightEdgeBesideTheModeSelector()
    {
        var (view, window) = await ShowAsync([new('h', "@@"), new('-', "-old", 1), new('+', "+new", null, 1)]);
        var selector = view.Descendant<ComboBox>("DiffModeSelector")!;
        var header = (Control)selector.GetVisualParent()!;
        var badge = (Control)view.Descendant<TextBlock>("ChangeLabel")!.GetVisualParent()!;
        var removed = view.Descendant<TextBlock>("RemovedLinesLabel")!;

        Assert.Equal(header.TranslatePoint(new(header.Bounds.Width, 0), view)!.Value.X, removed.TranslatePoint(new(removed.Bounds.Width, 0), view)!.Value.X, 1);
        Assert.Equal(Middle(selector, view), Middle(badge, view), 1);
        Assert.Equal(Middle(selector, view), Middle(removed, view), 1);
        window.Close();
    }

    private static double Middle(Control control, Visual to) => control.TranslatePoint(new(0, control.Bounds.Height / 2), to)!.Value.Y;

    private static async Task<(DiffView View, Window Window)> ShowAsync(PatchLine[] lines)
    {
        var view = new DiffView { Document = await new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "a.txt", default) };
        var window = new Window { Content = view, Width = 900, Height = 600 }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (view, window);
    }

    // The text a row draws after its line numbers, and where it starts.
    private static (string Text, double X) DrawnText(DiffRowControl row)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) row.Render(context);
        var runs = GlyphRuns(drawing, Matrix.Identity).Skip(1).ToArray();
        return (string.Concat(runs.Select(run => run.Text)), runs[0].X);
    }

    private static IEnumerable<(string Text, double X)> GlyphRuns(Drawing drawing, Matrix transform) => drawing switch
    {
        DrawingGroup group => group.Children.SelectMany(child => GlyphRuns(child, (group.Transform?.Value ?? Matrix.Identity) * transform)),
        GlyphRunDrawing { GlyphRun: { } run } => [(new string(run.Characters.Span), transform.Transform(run.BaselineOrigin).X)],
        _ => []
    };

    [AvaloniaFact]
    public void WhenTheSplitRatioIsSet_ShouldSizeTheColumnsByProportionAndKeepTheRatio()
    {
        var grid = new RatioGrid { Ratio = 0.6 };
        Assert.Equal(0.6, grid.ColumnDefinitions[0].Width.Value);
        grid.Ratio = 0.7;
        Assert.Equal(0.7, grid.Ratio, 6);
    }
}
