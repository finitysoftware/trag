using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Trag.Desktop.Controls;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Controls;

// One row of the commit graph as it draws itself: what Render puts into a drawing, read by brush, pen and bounds, since
// the headless platform turns no drawing into pixels.
public sealed class CommitGraphCellTests
{
    // The checked-out commit's dot (7 px, filled) wears a ring (12 px, outlined in the dot's color); another commit has
    // only its dot.
    [AvaloniaFact]
    public void WhenHeadIsOnTheRow_ShouldDrawTheRing()
    {
        var graph = CommitGraphLayout.Build([Snapshots.Commit(Snapshots.Id('b'), "Head", Snapshots.Id('a')), Snapshots.Commit(Snapshots.Id('a'), "Root")]);

        var head = Drawn(graph, Snapshots.Id('b'), Snapshots.Id('b'));
        var other = Drawn(graph, Snapshots.Id('a'), Snapshots.Id('b'));

        var dot = Assert.Single(head, drawing => drawing.Brush is not null);
        var ring = Assert.Single(head, drawing => drawing.Brush is null && drawing.Geometry!.Bounds.Size == new Size(12, 12));
        Assert.Equal(((ISolidColorBrush)dot.Brush!).Color, ((ISolidColorBrush)ring.Pen!.Brush!).Color);
        Assert.DoesNotContain(other, drawing => drawing.Geometry!.Bounds.Size == new Size(12, 12));
    }

    // A merge leaves its row with one line to each parent: straight down to the first, a curve into the lane of each
    // other one, so an octopus draws two curves.
    [AvaloniaFact]
    public void WhenAMergeRow_ShouldDrawOneCurvePerParent()
    {
        string[] parents = [Snapshots.Id('a'), Snapshots.Id('b'), Snapshots.Id('c')];
        var graph = CommitGraphLayout.Build([Snapshots.Commit(Snapshots.Id('o'), "Octopus", parents), .. parents.Select(parent => Snapshots.Commit(parent, "Parent"))]);

        var lines = Drawn(graph, Snapshots.Id('o'), null).Where(drawing => drawing.Brush is null).ToArray();

        Assert.Equal([0, 14, 28], lines.Select(line => line.Geometry!.Bounds.Width).Order());
    }

    // The cell of the commit, laid out as a row of the list, rendered into a drawing; answers what it drew.
    private static GeometryDrawing[] Drawn(CommitGraph graph, string id, string? head)
    {
        var cell = new CommitGraphCell { Commit = Snapshots.Commit(id, "Row"), Graph = graph, Head = head };
        cell.Measure(new Size(200, 24));
        cell.Arrange(new Rect(0, 0, CommitGraphCell.WidthFor(graph.LaneCount), 24));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) cell.Render(context);
        return [.. drawing.Children.OfType<GeometryDrawing>()];
    }
}
