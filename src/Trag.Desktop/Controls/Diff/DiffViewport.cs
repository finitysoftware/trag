using Avalonia.Input;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Controls.Diff;

// The arithmetic of the diff view, free of any control: which unified line a row shows in either mode and back, the
// gutter width for the line numbers, the top line for a scroll offset, the keyboard target of a row navigation key, and
// the horizontal scroll range of the visible columns.
public readonly record struct DiffViewport(DiffPresentation Document, bool IsSplit)
{
    public const double RowHeight = 22;
    // The divider between the two sides of a split row, and the padding around the text of a side.
    internal const double SplitDivider = 5;
    private const double TextPadding = 6;

    public int RowCount => IsSplit ? Document.SplitRows.Count : Document.Lines.Count;
    public double GutterWidth => IsSplit ? Document.NumberDigits * 7 + 8 : Document.NumberDigits * 14 + 16;

    // The unified line a row shows, -1 for a row that is not there, and the row that shows a unified line.
    public int ToSource(int row) => row < 0 || row >= RowCount ? -1 : IsSplit ? Document.ToUnified(row) : row;
    public int ToRow(int source) => IsSplit ? Document.ToSplit(source) : source;
    public static int TopRow(double verticalOffset) => (int)(verticalOffset / RowHeight);
    public int TopSource(double verticalOffset) => ToSource(TopRow(verticalOffset));

    // The changed lines chosen rows show, in order: in split mode a row shows both sides of its pair.
    public IReadOnlyList<int> ChangesIn(IEnumerable<int> rows)
    {
        var (document, split) = (Document, IsSplit);
        return rows.SelectMany(row => split ? [document.SplitRows[row].Left, document.SplitRows[row].Right] : new[] { row })
            .Where(source => source >= 0 && document.Lines[source].IsChange).Order().ToArray();
    }

    // Where the action of a block of unified lines sits: beside its first row, or beside the top row while the view is
    // scrolled into the block, and nowhere while no row of it is in view. In split mode a deletion and its addition share
    // a row, so the block takes the span of its lines' rows.
    public double? ActionTop(DiffBlock block, double verticalOffset, double height)
    {
        var rows = Enumerable.Range(block.Start, block.End - block.Start + 1).Select(ToRow).ToArray();
        var top = rows.Min() * RowHeight - verticalOffset;
        var bottom = (rows.Max() + 1) * RowHeight - verticalOffset;
        return bottom <= 0 || top >= height ? null : Math.Max(0, top);
    }

    // A page is the rows that fit, less one that stays in view.
    public static int PageSize(double height) => Math.Max(1, (int)(height / RowHeight) - 1);
    public static int? KeyTarget(Key key, int selected, int count, int page) => key switch
    {
        Key.Up => selected - 1, Key.Down => selected + 1,
        Key.PageUp => selected - page, Key.PageDown => selected + page,
        Key.Home => 0, Key.End => count - 1, _ => null
    };

    // The columns visible in one side and how far the text can scroll, in cells as wide as a row draws them; the row width
    // is used once a row has been laid out.
    public (double Viewport, double Maximum) HorizontalRange(double rowWidth, double controlWidth, double splitRatio, double cellWidth)
    {
        var width = Math.Max(1, rowWidth > 0 ? rowWidth : controlWidth - 16);
        var viewport = Math.Max(1, (IsSplit ? (width - SplitDivider) * Math.Min(splitRatio, 1 - splitRatio) : width) - GutterWidth - TextPadding);
        return (viewport, Math.Max(0, Document.MaxColumns * cellWidth + 24 - viewport));
    }
}
