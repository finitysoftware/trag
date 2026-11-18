using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Desktop.Controls.Diff;

internal readonly record struct DiffTextWindow(string Text, int Start, int Count)
{
    // The columns a shown tab adds to the one it stands in.
    private static readonly int TabExtra = FileDiffText.Tab.Length - 1;

    public string VisibleText => Text.Substring(Start, Count);

    // The column the visible text starts at, where a row draws it.
    public int StartColumn => FileDiffText.Columns(Text.AsSpan(0, Start));

    // lineEndings: the diff marks CRLF endings, because its lines end both ways.
    public static DiffTextWindow Create(PatchLine line, double offset, double width, double cellWidth, bool lineEndings)
    {
        var text = FileDiffText.Shown(line, lineEndings);
        var column = (int)(offset / cellWidth) - 2;
        var start = 0;
        for (var columns = 0; start < text.Length && columns < column; start++) columns += FileDiffText.Columns(text[start]);
        if (start < text.Length && char.IsLowSurrogate(text[start])) start = Math.Max(0, start - 1);
        var count = Math.Min(text.Length - start, Math.Clamp((int)(width / cellWidth) + 8, 8, 1024));
        if (count != 0 && start + count < text.Length && char.IsHighSurrogate(text[start + count - 1])) count--;
        return new(text, start, count);
    }

    public IEnumerable<SyntaxToken> VisibleTokens(string raw, IReadOnlyList<SyntaxToken> tokens)
    {
        var cursor = 0;
        var extraColumns = 0;
        foreach (var token in tokens)
        {
            extraColumns += raw.AsSpan(cursor, token.Start - cursor).Count('\t') * TabExtra;
            var tokenStart = token.Start + extraColumns;
            if (tokenStart >= Start + Count) yield break;
            extraColumns += raw.AsSpan(token.Start, token.Length).Count('\t') * TabExtra;
            cursor = token.Start + token.Length;
            var first = Math.Max(Start, tokenStart);
            var last = Math.Min(Start + Count, cursor + extraColumns);
            if (first < last) yield return new(first - Start, last - first, token.Kind);
        }
    }
}
