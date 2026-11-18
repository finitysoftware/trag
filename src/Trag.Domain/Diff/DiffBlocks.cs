namespace Trag.Domain.Diff;

// The hunks of a diff shown whole: each run of removed and added lines between unchanged ones.
public static class DiffBlocks
{
    // Whether chosen lines are exactly the changes of one run, which makes them its hunk.
    public static bool IsWhole(IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen) =>
        chosen.Count != 0 && At(lines, chosen[0]) is { } block && chosen.Order().SequenceEqual(block.Lines);

    // The run the line at the index belongs to; null for an unchanged line, a marker or an index outside the diff.
    public static DiffBlock? At(IReadOnlyList<PatchLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count || !lines[index].IsChange) return null;
        var start = index;
        while (start > 0 && lines[start - 1].IsChange) start--;
        var end = index;
        while (end + 1 < lines.Count && lines[end + 1].IsChange) end++;
        return new(start, end);
    }
}
