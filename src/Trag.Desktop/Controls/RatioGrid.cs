using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Trag.Desktop.Controls;

// One ratio owns both panes; dragging never feeds partially updated star sizes back into bindings.
// Ratio is what the user asked for (and what is remembered); EffectiveRatio is what the layout can honour right now.
// With a FirstPaneLength the first pane keeps that many pixels instead, however the grid itself grows or shrinks, and the
// second pane takes the rest; a grid shorter than the length gives the first pane all it has, so the splitter stays on
// its edge. A negative length asks for the default ratio of the first size the grid gets, which then
// becomes the length. The splitter moves either through Move.
public sealed class RatioGrid : Grid
{
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<RatioGrid, double>(nameof(Ratio), 0.5,
        defaultBindingMode: BindingMode.TwoWay, coerce: (sender, value) => ((RatioGrid)sender).Clamp(value));
    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<RatioGrid, Orientation>(nameof(Orientation));
    // The first pane never gets less than this share; 0 keeps the old behaviour of collapsing to either edge.
    public static readonly StyledProperty<double> MinRatioProperty = AvaloniaProperty.Register<RatioGrid, double>(nameof(MinRatio));
    // Double-click and Enter on the splitter go here.
    public static readonly StyledProperty<double> DefaultRatioProperty = AvaloniaProperty.Register<RatioGrid, double>(nameof(DefaultRatio), 0.5);
    // The second pane is never squeezed below the size it asks for on its own (measured with unlimited length).
    public static readonly StyledProperty<bool> KeepSecondPaneNaturalSizeProperty = AvaloniaProperty.Register<RatioGrid, bool>(nameof(KeepSecondPaneNaturalSize));
    // With the second pane collapsed the first one takes everything and the splitter track disappears.
    public static readonly StyledProperty<bool> IsSecondPaneCollapsedProperty = AvaloniaProperty.Register<RatioGrid, bool>(nameof(IsSecondPaneCollapsed));
    public static readonly StyledProperty<double> FirstPaneLengthProperty = AvaloniaProperty.Register<RatioGrid, double>(nameof(FirstPaneLength), double.NaN,
        defaultBindingMode: BindingMode.TwoWay);
    // The track between the panes; PaneSplitter paints only a thin line in its middle.
    public const double SplitterThickness = 6;
    private double secondPaneNatural;
    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public double MinRatio { get => GetValue(MinRatioProperty); set => SetValue(MinRatioProperty, value); }
    public double DefaultRatio { get => GetValue(DefaultRatioProperty); set => SetValue(DefaultRatioProperty, value); }
    public bool KeepSecondPaneNaturalSize { get => GetValue(KeepSecondPaneNaturalSizeProperty); set => SetValue(KeepSecondPaneNaturalSizeProperty, value); }
    public bool IsSecondPaneCollapsed { get => GetValue(IsSecondPaneCollapsedProperty); set => SetValue(IsSecondPaneCollapsedProperty, value); }
    public double FirstPaneLength { get => GetValue(FirstPaneLengthProperty); set => SetValue(FirstPaneLengthProperty, value); }
    public double EffectiveRatio { get; private set; } = 0.5;
    public RatioGrid() { ClipToBounds = true; Configure(); }
    private double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Math.Clamp(MinRatio, 0, 1), 1) : Math.Clamp(DefaultRatio, 0, 1);
    private double Length => (Orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width) - SplitterThickness;
    private bool HoldsLength => !double.IsNaN(FirstPaneLength);

    // Where the splitter puts the line, as a share of the grid: the ratio itself, or the length it stands for.
    public void Move(double ratio)
    {
        if (!HoldsLength) { SetCurrentValue(RatioProperty, ratio); return; }
        SetCurrentValue(FirstPaneLengthProperty, Math.Clamp(ratio, 0, 1) * Math.Max(0, Length));
    }
    private void Configure()
    {
        var track = SplitterThickness.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Orientation == Orientation.Vertical) { ColumnDefinitions = new("*"); RowDefinitions = new("*," + track + ",*"); }
        else ColumnDefinitions = new("*," + track + ",*");
        ApplyRatio();
    }
    private void ApplyRatio()
    {
        if (HoldsLength && FirstPaneLength >= 0) { ApplyLength(); return; }
        var ratio = HoldsLength ? DefaultRatio : Ratio;
        var length = Length;
        if (KeepSecondPaneNaturalSize && length > 0 && secondPaneNatural > 0) ratio = Math.Min(ratio, Math.Max(Math.Clamp(MinRatio, 0, 1), 1 - secondPaneNatural / length));
        EffectiveRatio = ratio;
        var collapsed = IsSecondPaneCollapsed;
        var first = new GridLength(collapsed ? 1 : ratio, GridUnitType.Star);
        var track = collapsed ? new GridLength(0) : new GridLength(SplitterThickness);
        var second = collapsed ? new GridLength(0) : new GridLength(1 - ratio, GridUnitType.Star);
        if (Orientation == Orientation.Vertical) { RowDefinitions[0].Height = first; RowDefinitions[1].Height = track; RowDefinitions[2].Height = second; }
        else { ColumnDefinitions[0].Width = first; ColumnDefinitions[1].Width = track; ColumnDefinitions[2].Width = second; }
    }
    private void ApplyLength()
    {
        var first = Math.Min(FirstPaneLength, Math.Max(0, Length));
        EffectiveRatio = Length > 0 ? first / Length : DefaultRatio;
        var track = new GridLength(SplitterThickness);
        if (Orientation == Orientation.Vertical) { RowDefinitions[0].Height = new(first); RowDefinitions[1].Height = track; RowDefinitions[2].Height = new(1, GridUnitType.Star); }
        else { ColumnDefinitions[0].Width = new(first); ColumnDefinitions[1].Width = track; ColumnDefinitions[2].Width = new(1, GridUnitType.Star); }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (KeepSecondPaneNaturalSize && Children.Count >= 3 && Children[2] is Control second)
        {
            second.Measure(Orientation == Orientation.Vertical ? new Size(availableSize.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, availableSize.Height));
            var natural = Orientation == Orientation.Vertical ? second.DesiredSize.Height : second.DesiredSize.Width;
            if (Math.Abs(natural - secondPaneNatural) > 0.5) { secondPaneNatural = natural; ApplyRatio(); }
        }
        return base.MeasureOverride(availableSize);
    }
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // A length chosen before the grid had a size, or a grid that changed size, moves the line's share; the splitter
        // starts from that share.
        if (HoldsLength && FirstPaneLength < 0 && Length > 0) SetCurrentValue(FirstPaneLengthProperty, DefaultRatio * Length);
        else if (HoldsLength || KeepSecondPaneNaturalSize) ApplyRatio();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OrientationProperty) Configure();
        else if (change.Property == RatioProperty || change.Property == IsSecondPaneCollapsedProperty || change.Property == KeepSecondPaneNaturalSizeProperty
            || change.Property == FirstPaneLengthProperty) ApplyRatio();
        else if (change.Property == MinRatioProperty) { CoerceValue(RatioProperty); ApplyRatio(); }
    }
}
