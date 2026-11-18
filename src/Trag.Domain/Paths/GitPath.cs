namespace Trag.Domain.Paths;

// Raw bytes preserve Linux paths that cannot be represented as UTF-8.
public sealed record GitPath(byte[] Bytes)
{
    public string Display => System.Text.Encoding.UTF8.GetString(Bytes);

    // The display text as a list row shows it: backslashes, control characters, line and paragraph separators and the marks
    // that turn the text's direction escaped, so a path never breaks the row or disguises its name. Joiners a script needs
    // stay.
    public string DisplayEscaped => string.Concat(Display.Select(Escaped));

    private static string Escaped(char c) => c switch
    {
        '\\' => "\\\\", '\r' => "\\r", '\n' => "\\n", '\t' => "\\t",
        _ when char.IsControl(c) || c is '\u2028' or '\u2029' or '\u061C' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069')
            => "\\u" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
        _ => c.ToString()
    };

    // Whether the display text encodes back to exactly these bytes. Only such a path may be handed to Git as text;
    // a replacement character in the display would name a different file.
    public bool IsDisplayable => System.Text.Encoding.UTF8.GetBytes(Display).AsSpan().SequenceEqual(Bytes);

    // The identity of a path for sets and lookups: its bytes, never its display text. A record compares the array by
    // reference, so two reads of one path are equal only through these.
    public string Key => Convert.ToHexString(Bytes);
    // The order Git lists paths in: by their bytes.
    public static readonly Comparer<GitPath> ByBytes = Comparer<GitPath>.Create((x, y) => x.Bytes.AsSpan().SequenceCompareTo(y.Bytes));
    public bool IsSame(GitPath? other) => other is not null && Bytes.AsSpan().SequenceEqual(other.Bytes);
    // Two paths either of which may be absent, as a rename's original path.
    public static bool Same(GitPath? first, GitPath? second) => first is null ? second is null : first.IsSame(second);
}
