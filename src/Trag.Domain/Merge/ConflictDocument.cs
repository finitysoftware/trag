using System.Text;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Merge;

// A conflicted working file as Git left it: its lines with their own endings, and the conflicts between Git's markers at
// the start of a line, as long as the file's conflict-marker-size attribute says (seven unless it says otherwise):
// <<<<<<< opens our side, ||||||| starts the base (diff3, always with the base's name after it), ======= starts their
// side, >>>>>>> closes the conflict. A conflict that never closes means the file is not Git's any more, and so does one
// the text alone cannot divide: a second separator or base marker, or a closing marker again before the next conflict
// opens (a Markdown underline, a quoted e-mail reply), since either side may hold that line, which Git's rerere cannot
// tell either. Parse answers null and the file is left to the
// editor. Take rewrites one conflict as one side, the other or both, and keeps every other byte of the file as it was.
public sealed class ConflictDocument
{
    private readonly IReadOnlyList<string> lines;

    private ConflictDocument(IReadOnlyList<string> lines, IReadOnlyList<ConflictBlock> blocks)
    {
        this.lines = lines;
        Blocks = blocks;
    }

    public IReadOnlyList<ConflictBlock> Blocks { get; }

    // Git's marker length when no conflict-marker-size attribute sets another.
    public const int DefaultMarkerSize = 7;

    public static ConflictDocument? Parse(string text, int markerSize = DefaultMarkerSize)
    {
        var lines = Split(text);
        var blocks = new List<ConflictBlock>();
        int start = -1, middle = -1, separator = -1;
        var closed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (start < 0)
            {
                if (IsMarker(line, '<', markerSize)) (start, closed) = (i, false);
                else if (closed && IsMarker(line, '>', markerSize)) return null;
                continue;
            }
            if (IsMarker(line, '<', markerSize)) return null;
            if (separator < 0)
            {
                if (IsBase(line, markerSize))
                {
                    if (middle >= 0) return null;
                    middle = i;
                }
                else if (IsSeparator(line, markerSize)) separator = i;
                continue;
            }
            if (IsSeparator(line, markerSize)) return null;
            if (!IsMarker(line, '>', markerSize)) continue;
            blocks.Add(new(start, middle, separator, i));
            (start, middle, separator, closed) = (-1, -1, -1, true);
        }
        return start < 0 ? new(lines, blocks) : null;
    }

    public string Take(int block, ConflictChoice choice)
    {
        var chosen = Blocks[block];
        var text = new StringBuilder();
        for (var i = 0; i < chosen.Start; i++) text.Append(lines[i]);
        if (choice != ConflictChoice.Theirs) for (var i = chosen.Start + 1; i < chosen.MineEnd; i++) text.Append(lines[i]);
        if (choice != ConflictChoice.Mine) for (var i = chosen.Separator + 1; i < chosen.End; i++) text.Append(lines[i]);
        for (var i = chosen.End + 1; i < lines.Count; i++) text.Append(lines[i]);
        return text.ToString();
    }

    // The conflicts with a few unchanged lines around each, in file order, so a large file shows only what needs a decision.
    public IReadOnlyList<ConflictPart> Parts()
    {
        const int context = 3;
        var parts = new List<ConflictPart>();
        var shown = 0;
        for (var b = 0; b < Blocks.Count; b++)
        {
            var block = Blocks[b];
            var from = Math.Max(shown, block.Start - context);
            if (from < block.Start) parts.Add(new(Range(from, block.Start), from > shown));
            parts.Add(new([], from > shown && from == block.Start, b, MergeText.BlockTitle(b, Blocks.Count),
                Range(block.Start + 1, block.MineEnd), Range(block.Separator + 1, block.End)));
            shown = block.End + 1;
            var until = Math.Min(shown + context, b + 1 < Blocks.Count ? Blocks[b + 1].Start : lines.Count);
            if (until > shown) parts.Add(new(Range(shown, until), false));
            shown = Math.Max(shown, until);
        }
        return parts;
    }

    private IReadOnlyList<ConflictLine> Range(int from, int to) =>
        Enumerable.Range(from, to - from).Select(i => new ConflictLine(i + 1, lines[i].TrimEnd('\n').TrimEnd('\r'))).ToArray();

    // Every line keeps its own ending, so a rewrite gives back the file's CRLF and LF exactly as they were.
    private static List<string> Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    // Every conflict Git leaves, closed or not, has the line that opens its block.
    public static bool OpensBlock(string line, int markerSize) => IsMarker(line, '<', markerSize);

    private static bool IsMarker(string line, char mark, int size) =>
        line.Length >= size && line.AsSpan(0, size).IndexOfAnyExcept(mark) < 0 && (line.Length == size || line[size] is ' ' or '\t' or '\r' or '\n');

    // Git names the base after its marker: its commit, "base", "parent of …" or "Stash base".
    private static bool IsBase(string line, int size) => IsMarker(line, '|', size) && !line.AsSpan(size).IsWhiteSpace();

    private static bool IsSeparator(string line, int size)
    {
        var text = line.AsSpan().TrimEnd('\n').TrimEnd('\r');
        return text.Length == size && text.IndexOfAnyExcept('=') < 0;
    }
}
