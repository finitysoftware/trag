using System.Globalization;
using Trag.Domain.Diff;

namespace Trag.Infrastructure.Git.Parsing;

internal sealed class PatchHunk
{
    private int? oldNumber;
    private int? newNumber;

    public PatchLine ReadLine(string text, bool carriageReturn)
    {
        if (text.StartsWith("diff --git ", StringComparison.Ordinal)) oldNumber = newNumber = null;
        // A hunk header that cannot be read, as one the output limit cut short, numbers no line under it.
        if (text.StartsWith("@@ ", StringComparison.Ordinal))
        {
            (oldNumber, newNumber) = text.Split(' ', 4) is [_, var from, var to, ..] && Number(from) is { } old && Number(to) is { } now ? (old, now) : default((int?, int?));
            return new('h', text);
        }
        if (oldNumber is null) return new('h', text);
        // diff.suppressBlankEmpty prints an unchanged blank line without its space; inside a hunk it is still a line of the
        // file, on both sides.
        if (text.Length == 0) return ReadContent(" ", carriageReturn);
        if (text[0] is not ('+' or '-' or ' ')) return new('h', text);
        return ReadContent(text, carriageReturn);
    }

    private PatchLine ReadContent(string text, bool carriageReturn)
    {
        var kind = text[0];
        int? left = null, right = null;
        if (kind != '+') left = oldNumber++;
        if (kind != '-') right = newNumber++;
        return new(kind, text, left, right) { HasCarriageReturn = carriageReturn };
    }

    private static int? Number(string range) =>
        range is [_, .. var start] && int.TryParse(start.Split(',')[0], CultureInfo.InvariantCulture, out var number) ? number : null;
}
