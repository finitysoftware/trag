using Trag.Domain.Diff;

namespace Trag.Domain.Presentation;

// Both projections share the same display lines; file content is not duplicated.
public sealed record DiffPresentation(IReadOnlyList<PatchLine> Lines, IReadOnlyList<DiffPair> SplitRows,
    IReadOnlyList<int> UnifiedToSplit, IReadOnlyList<SyntaxToken[]> OldTokens,
    IReadOnlyList<SyntaxToken[]> NewTokens, string SyntaxNotice)
{
    // The content this is a presentation of; none for the empty presentation shown while nothing is selected.
    public DiffSource? Source { get; init; }
    public int MaxColumns { get; init; }
    public int NumberDigits { get; init; } = 1;
    // The badge's word for this change and the two line counts are FileDiffText's; the two flags are what the view styles on.
    public string ChangeLabel { get; init; } = "";
    public string AddedText { get; init; } = "";
    public string RemovedText { get; init; } = "";
    public bool IsNewFile { get; init; }
    public bool IsDeletedFile { get; init; }
    // Whether the rows mark a line Git ends with CRLF: only in a diff whose lines end both with CRLF and with LF.
    public bool ShowsLineEndings { get; init; }
    public static DiffPresentation Empty { get; } = new([], [], [], [], [], "");
    public int ToSplit(int source) => source < 0 || Lines.Count == 0 ? -1 : UnifiedToSplit[Math.Min(source, Lines.Count - 1)];
    public int ToUnified(int row)
    {
        if (row < 0 || SplitRows.Count == 0) return -1;
        var pair = SplitRows[Math.Min(row, SplitRows.Count - 1)];
        return pair.Left < 0 ? pair.Right : pair.Left;
    }

    // The line equal to the given one, looked for outward from where it was: in the same content read again it is at or
    // near that index, and a line that is gone is simply not found. Equal lines repeat only as omission markers, and
    // then the nearest one is the right one.
    public int IndexOf(PatchLine line, int near)
    {
        if (Lines.Count == 0) return -1;
        near = Math.Clamp(near, 0, Lines.Count - 1);
        for (var distance = 0; distance < Lines.Count; distance++)
        {
            var below = near + distance;
            var above = near - distance;
            if (below < Lines.Count && Lines[below] == line) return below;
            if (above >= 0 && above != below && Lines[above] == line) return above;
        }
        return -1;
    }
}
