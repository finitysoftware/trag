using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Trag.Desktop.Themes;
using Trag.Domain.Diff;

namespace Trag.Desktop.Controls.Diff;

// Only realized rows own glyph layouts: one text and one number layout per side, built for the visible horizontal
// window and the theme, and released as soon as the row is replaced or leaves the tree, because Skia keeps native text
// behind a layout until it is disposed or finalized. A row reads the document, the identity of the current rows, the
// split ratio, the horizontal offset and the gutter width from the view that owns it.
public sealed class DiffRowControl(DiffView owner, DiffRowData data) : Control
{
    private static readonly Typeface Font = new("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");
    private static readonly IBrush Added = new ImmutableSolidColorBrush(Color.Parse("#2443A477"));
    private static readonly IBrush Removed = new ImmutableSolidColorBrush(Color.Parse("#24DB6464"));
    private static readonly IBrush Header = new ImmutableSolidColorBrush(Color.Parse("#14438ADB"));
    private static readonly IBrush Blank = new ImmutableSolidColorBrush(Color.Parse("#087F7F7F"));
    private static readonly IBrush DarkText = new ImmutableSolidColorBrush(Color.Parse("#DCE1E8"));
    private static readonly IBrush LightText = new ImmutableSolidColorBrush(Color.Parse("#252B34"));
    internal static readonly double CellWidth = MeasureCell();
    private readonly SideText left = new();
    private readonly SideText right = new();

    private static double MeasureCell()
    {
        using var cell = new TextLayout("M", Font, 12, LightText);
        return cell.Width;
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, DiffViewport.RowHeight);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Release();
    }

    // The glyph layouts go now rather than with a later collection; a row still shown builds them again when it renders.
    public void Release()
    {
        left.Dispose();
        right.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!ReferenceEquals(data.Identity, owner.RowIdentity)) return;
        if (data.Split)
        {
            var pair = owner.Document.SplitRows[data.Index];
            var leftWidth = Math.Max(0, Bounds.Width - DiffViewport.SplitDivider) * owner.SplitRatio;
            DrawSide(context, pair.Left, new(0, 0, leftWidth, DiffViewport.RowHeight), false, left);
            DrawSide(context, pair.Right, new(leftWidth + DiffViewport.SplitDivider, 0, Math.Max(0, Bounds.Width - leftWidth - DiffViewport.SplitDivider), DiffViewport.RowHeight), true, right);
            context.FillRectangle(Blank, new(leftWidth, 0, DiffViewport.SplitDivider, DiffViewport.RowHeight));
        }
        else DrawSide(context, data.Index, new(0, 0, Bounds.Width, DiffViewport.RowHeight), !owner.Document.Lines[data.Index].IsDeletion, left);
    }

    private void DrawSide(DrawingContext context, int index, Rect bounds, bool right, SideText side)
    {
        using var clip = context.PushClip(bounds);
        if (index < 0)
        {
            context.FillRectangle(Blank, bounds);
            return;
        }
        var line = owner.Document.Lines[index];
        context.FillRectangle(Background(line), bounds);
        var dark = AppThemes.IsDark(ActualThemeVariant);
        var brush = dark ? DarkText : LightText;
        var textBounds = new Rect(bounds.X + owner.GutterWidth, 0, Math.Max(0, bounds.Width - owner.GutterWidth), bounds.Height);
        var window = DiffTextWindow.Create(line, owner.HorizontalOffset, textBounds.Width, CellWidth, owner.Document.ShowsLineEndings);
        if (!side.IsFor(dark, window)) side.Replace(dark, window, NumbersOf(line, right, brush), TextOf(window, line, index, right, dark, brush));
        // The numbers are padded to their two columns, so the width that aligns them to the right counts the trailing spaces.
        using (context.PushOpacity(0.45)) side.Numbers!.Draw(context, new(bounds.X + owner.GutterWidth - side.Numbers.WidthIncludingTrailingWhitespace - 4, 3));
        using var textClip = context.PushClip(textBounds);
        side.Text!.Draw(context, new(textBounds.X + 4 + window.StartColumn * CellWidth - owner.HorizontalOffset, 2));
    }

    private static IBrush Background(PatchLine line)
    {
        if (line.IsAddition) return Added;
        if (line.IsDeletion) return Removed;
        if (line.IsHeader) return Header;
        return Brushes.Transparent;
    }

    private TextLayout NumbersOf(PatchLine line, bool right, IBrush brush) =>
        new(data.Numbers(line, right, owner.Document.NumberDigits), Font, 11, brush);

    // The run properties of the colored spans are made per layout: they resolve the typeface through the font manager
    // of the moment, so they are never kept beyond the layout they style.
    private TextLayout TextOf(DiffTextWindow window, PatchLine line, int index, bool right, bool dark, IBrush brush)
    {
        var tokens = (right ? owner.Document.NewTokens : owner.Document.OldTokens)[index];
        var colors = SyntaxColors.For(dark);
        var styles = new TextRunProperties?[colors.Length];
        var colored = window.VisibleTokens(line.Text, tokens)
            .Select(token => new ValueSpan<TextRunProperties>(token.Start, token.Length,
                styles[(int)token.Kind] ??= new GenericTextRunProperties(Font, 12, foregroundBrush: colors[(int)token.Kind]))).ToArray();
        return new(window.VisibleText, Font, 12, brush, textStyleOverrides: colored.Length == 0 ? null : colored);
    }

    // The glyph layouts of one side of a row, valid for one theme and one horizontal window.
    private sealed class SideText : IDisposable
    {
        private (bool Dark, int Start, int Count) key;
        public TextLayout? Numbers { get; private set; }
        public TextLayout? Text { get; private set; }

        public bool IsFor(bool dark, DiffTextWindow window) => Text is not null && key == (dark, window.Start, window.Count);

        public void Replace(bool dark, DiffTextWindow window, TextLayout numbers, TextLayout text)
        {
            Dispose();
            key = (dark, window.Start, window.Count);
            Numbers = numbers;
            Text = text;
        }

        public void Dispose()
        {
            Numbers?.Dispose();
            Text?.Dispose();
            Numbers = null;
            Text = null;
        }
    }
}
