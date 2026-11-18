using Trag.Domain.Diff;

namespace Trag.Desktop.Controls.Diff;

// Where the user is in a diff, kept as lines rather than rows: the line at the top with how far into it the view is
// scrolled, and the selected line. The same lines are looked for in the rows shown next, so the place survives a mode
// switch and a re-read of the same content, and a line that is gone is simply not found.
internal sealed record DiffAnchor(PatchLine Top, int TopIndex, double Fraction, PatchLine? Selected, int SelectedIndex)
{
    public static DiffAnchor? Capture(DiffViewport shown, double verticalOffset, int selectedSource)
    {
        var top = shown.TopSource(verticalOffset);
        if (top < 0) return null;
        var lines = shown.Document.Lines;
        var selected = selectedSource >= 0 && selectedSource < lines.Count ? lines[selectedSource] : null;
        return new(lines[top], top, verticalOffset % DiffViewport.RowHeight, selected, selectedSource);
    }

    // The place in other rows: the row of the same top line at the same fraction, or the start when that line is gone,
    // and the same selected line where it still is, or no selection.
    public DiffPlacement Locate(DiffViewport target)
    {
        var top = target.Document.IndexOf(Top, TopIndex);
        var selected = Selected is null ? -1 : target.Document.IndexOf(Selected, SelectedIndex);
        var offset = top < 0 ? 0 : target.ToRow(top) * DiffViewport.RowHeight + Fraction;
        return new(offset, selected);
    }
}
