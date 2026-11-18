using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Trag.Desktop.Controls.Icons;

namespace Trag.Desktop.Controls;

// A stroked glyph from IconGeometry, scaled to the control's size.
public sealed class LineIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<LineIcon, string>(nameof(Kind), "file");
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<LineIcon, IBrush?>(nameof(Foreground), Brushes.Gray);
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    static LineIcon() => AffectsRender<LineIcon>(KindProperty, ForegroundProperty);
    // Tests check that every Kind used in XAML exists; an unknown kind draws the file glyph.
    internal static IReadOnlyCollection<string> Kinds => IconGeometry.Kinds;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24));
        context.DrawGeometry(null, new Pen(Foreground, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), IconGeometry.Get(Kind));
    }
}
