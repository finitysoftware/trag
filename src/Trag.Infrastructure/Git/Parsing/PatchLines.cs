using Trag.Domain.Diff;

namespace Trag.Infrastructure.Git.Parsing;

// The rows a diff or a content preview may hold: at most LineLimit of them, each clipped to LineLengthLimit characters.
// Once any limit was hit the whole result is limited: a summary shows no rows and offers a preview, and a preview shows
// the first PreviewLines rows. Only the rows and the limits live here; what a row means stays with the parser.
internal sealed class PatchLines(bool limited)
{
    private readonly List<PatchLine> lines = [];
    private int rows;

    public bool Limited { get; private set; } = limited;
    public int Count => lines.Count;

    // True when the row limit is reached: the result is limited and reading stops there. Git's headers are not rows.
    public bool IsFull()
    {
        if (rows < DiffLimits.LineLimit) return false;
        Limited = true;
        return true;
    }

    public string Clip(string text)
    {
        if (text.Length <= DiffLimits.LineLengthLimit) return text;
        Limited = true;
        return text[..DiffLimits.LineLengthLimit] + "…";
    }

    public void Add(PatchLine line)
    {
        lines.Add(line);
        if (!line.IsHeader) rows++;
    }

    // Git's "\ No newline at end of file" describes the row before it; with no row before it, it is content.
    public bool RemoveTerminatorOfLast()
    {
        if (lines.Count == 0) return false;
        lines[^1] = lines[^1].WithoutTerminator();
        return true;
    }

    public FileDiff ToDiff(IReadOnlyList<FileDiffMetadata> metadata, bool preview, string limitedPreview, string limitedSummary, string text)
    {
        if (!Limited) return new(metadata, lines, DiffContentKind.Text, text);
        return new(metadata, preview ? lines.Take(DiffLimits.PreviewLines).ToArray() : [], DiffContentKind.Limited,
            preview ? limitedPreview : limitedSummary, !preview);
    }
}
