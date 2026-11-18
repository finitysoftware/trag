using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Trag.Domain.History;

namespace Trag.Desktop.Controls;

// One row of the commit graph: lines from the previous row into this row, the node, and lines towards the next row.
public sealed class CommitGraphCell : Control
{
    public const double LaneWidth = 14;
    private const double SidePadding = 5;
    public static readonly StyledProperty<HistoryCommit?> CommitProperty = AvaloniaProperty.Register<CommitGraphCell, HistoryCommit?>(nameof(Commit));
    public static readonly StyledProperty<CommitGraph?> GraphProperty = AvaloniaProperty.Register<CommitGraphCell, CommitGraph?>(nameof(Graph));
    // The checked-out commit: its dot wears a ring, so the current position stays visible while moving through history.
    public static readonly StyledProperty<string?> HeadProperty = AvaloniaProperty.Register<CommitGraphCell, string?>(nameof(Head));
    private static readonly IBrush[] Palette = new[] { "#E07A3F", "#3F9D5C", "#3C7DD9", "#B45BC4", "#D9A63C", "#2FA8A8", "#D94F6E", "#7A8A3F" }
        .Select(hex => (IBrush)new SolidColorBrush(Color.Parse(hex)).ToImmutable()).ToArray();
    private static readonly IPen[] Pens = Palette.Select(brush => (IPen)new Pen(brush, 2, lineCap: PenLineCap.Round).ToImmutable()).ToArray();
    private static readonly IPen[] Rings = Palette.Select(brush => (IPen)new Pen(brush, 1.5).ToImmutable()).ToArray();
    public HistoryCommit? Commit { get => GetValue(CommitProperty); set => SetValue(CommitProperty, value); }
    public CommitGraph? Graph { get => GetValue(GraphProperty); set => SetValue(GraphProperty, value); }
    public string? Head { get => GetValue(HeadProperty); set => SetValue(HeadProperty, value); }
    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(CommitProperty, GraphProperty, HeadProperty);
        AffectsMeasure<CommitGraphCell>(GraphProperty);
    }
    public static double WidthFor(int lanes) => Math.Max(1, lanes) * LaneWidth + SidePadding * 2;
    // The width the cells of a graph take, for the column header above them.
    public static readonly IValueConverter WidthOfGraph = new FuncValueConverter<CommitGraph?, double>(graph => WidthFor(graph?.LaneCount ?? 1));
    protected override Size MeasureOverride(Size availableSize) => new(WidthFor(Graph?.LaneCount ?? 1), 0);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Graph is null || Commit is null || Graph.For(Commit.Id) is not { } row) return;
        var height = Bounds.Height;
        var middle = height / 2;
        foreach (var edge in row.Incoming) Draw(context, X(edge.FromLane), 0, X(edge.ToLane), middle, edge.Color);
        foreach (var edge in row.Outgoing) Draw(context, X(edge.FromLane), middle, X(edge.ToLane), height, edge.Color);
        context.DrawEllipse(Palette[row.Color % Palette.Length], null, new Point(X(row.Lane), middle), 3.5, 3.5);
        if (Head is { } head && head == Commit.Id) context.DrawEllipse(null, Rings[row.Color % Rings.Length], new Point(X(row.Lane), middle), 6, 6);
    }
    private static double X(int lane) => SidePadding + lane * LaneWidth + LaneWidth / 2;
    private static void Draw(DrawingContext context, double x1, double y1, double x2, double y2, int color)
    {
        var pen = Pens[color % Pens.Length];
        if (Math.Abs(x1 - x2) < 0.5) { context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2)); return; }
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(new Point(x1, y1), false);
            figure.CubicBezierTo(new Point(x1, (y1 + y2) / 2), new Point(x2, (y1 + y2) / 2), new Point(x2, y2));
            figure.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
