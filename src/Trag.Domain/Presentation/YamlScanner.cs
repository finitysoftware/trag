namespace Trag.Domain.Presentation;

// YAML one line at a time: after the indentation and any list dashes, a key up to the colon that a space or the line's
// end follows, then the value: a quoted string, a word YAML gives a meaning, a number, or any other scalar as a string.
// A hash starts a comment at the start of the line or after a space, and not inside a quoted value.
internal static class YamlScanner
{
    public static void Scan(string text, int start, List<SyntaxToken> tokens)
    {
        var at = Skip(text, start);
        while (at < text.Length && text[at] == '-' && (at + 1 == text.Length || text[at + 1] == ' ')) at = Skip(text, at + 1);
        if (at < text.Length && text[at] != '#' && Colon(text, at) is var colon and >= 0)
        {
            tokens.Add(new(at, text.AsSpan(at, colon - at).TrimEnd().Length, SyntaxKind.Member));
            at = Skip(text, colon + 1);
        }
        if (at < text.Length && text[at] != '#') at = Value(text, at, tokens);
        at = Skip(text, at);
        if (at < text.Length && text[at] == '#') tokens.Add(new(at, text.Length - at, SyntaxKind.Comment));
    }

    // The value, quoted to its closing quote, or plain up to a comment or the end of the line.
    private static int Value(string text, int at, List<SyntaxToken> tokens)
    {
        if (text[at] is '"' or '\'')
        {
            var close = text.IndexOf(text[at], at + 1);
            var end = close < 0 ? text.Length : close + 1;
            tokens.Add(new(at, end - at, SyntaxKind.String));
            return end;
        }
        var stop = text.IndexOf(" #", at, StringComparison.Ordinal);
        var plain = text.AsSpan(at, (stop < 0 ? text.Length : stop) - at).TrimEnd();
        if (plain.IsEmpty) return at;
        var kind = plain switch
        {
            "true" or "false" or "True" or "False" or "null" or "Null" or "~" or "yes" or "no" => SyntaxKind.Keyword,
            _ when char.IsAsciiDigit(plain[0]) && double.TryParse(plain, System.Globalization.CultureInfo.InvariantCulture, out _) => SyntaxKind.Number,
            _ => SyntaxKind.String
        };
        tokens.Add(new(at, plain.Length, kind));
        return at + plain.Length;
    }

    // The colon that ends a key: one that a space or the end of the line follows, before any comment.
    private static int Colon(string text, int at)
    {
        for (var index = at; index < text.Length; index++)
        {
            if (text[index] == '#' && text[index - 1] == ' ' || text[index] is '"' or '\'' && index == at) return -1;
            if (text[index] == ':' && (index + 1 == text.Length || text[index + 1] == ' ')) return index;
        }
        return -1;
    }

    private static int Skip(string text, int at)
    {
        while (at < text.Length && text[at] == ' ') at++;
        return at;
    }
}
