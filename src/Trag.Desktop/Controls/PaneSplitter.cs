using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Trag.Desktop.Controls;

// The handle remains reachable at either edge, even with one pane completely hidden.
// Its track is RatioGrid.SplitterThickness wide so it stays easy to grab, but only a 2 px line in the middle is painted.
public sealed class PaneSplitter : Control
{
    public const double LineThickness = 2;
    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<PaneSplitter, IBrush?>(nameof(Background));
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    static PaneSplitter() => AffectsRender<PaneSplitter>(BackgroundProperty);
    private double? startRatio;
    private double startPosition;
    private RatioGrid? Pane => Parent as RatioGrid;
    public PaneSplitter()
    {
        Focusable = true;
        AttachedToVisualTree += (_, _) => Cursor = new(Pane?.Orientation == Orientation.Vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        DoubleTapped += (_, e) => { Pane?.Move(Pane.DefaultRatio); e.Handled = true; };
    }
    public override void Render(DrawingContext context)
    {
        // The transparent fill keeps the whole track hit-testable; the visible line is centred in it.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Background is null) return;
        var vertical = Pane?.Orientation == Orientation.Vertical;
        var line = vertical
            ? new Rect(0, Math.Max(0, (Bounds.Height - LineThickness) / 2), Bounds.Width, Math.Min(LineThickness, Bounds.Height))
            : new Rect(Math.Max(0, (Bounds.Width - LineThickness) / 2), 0, Math.Min(LineThickness, Bounds.Width), Bounds.Height);
        context.FillRectangle(Background, line);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Pane is not { } pane || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); startRatio = pane.EffectiveRatio;
        var point = e.GetPosition(pane); startPosition = pane.Orientation == Orientation.Vertical ? point.Y : point.X;
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (startRatio is not { } initial || Pane is not { } pane) return;
        var point = e.GetPosition(pane);
        var size = (pane.Orientation == Orientation.Vertical ? pane.Bounds.Height : pane.Bounds.Width) - RatioGrid.SplitterThickness;
        var position = pane.Orientation == Orientation.Vertical ? point.Y : point.X;
        if (size > 0) pane.Move(initial + (position - startPosition) / size);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); startRatio = null; e.Pointer.Capture(null); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); startRatio = null; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Pane is not { } pane) return;
        var vertical = pane.Orientation == Orientation.Vertical;
        var size = Math.Max(1, (vertical ? pane.Bounds.Height : pane.Bounds.Width) - RatioGrid.SplitterThickness);
        var delta = e.Key == (vertical ? Key.Up : Key.Left) ? -16 : e.Key == (vertical ? Key.Down : Key.Right) ? 16 : 0;
        var ratio = e.Key switch { Key.Home => 0, Key.End => 1, Key.Enter => pane.DefaultRatio, _ => pane.EffectiveRatio + delta / size };
        if (delta == 0 && e.Key is not (Key.Home or Key.End or Key.Enter)) return;
        pane.Move(ratio); e.Handled = true;
    }
}
