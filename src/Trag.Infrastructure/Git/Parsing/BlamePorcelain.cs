using System.Globalization;
using System.Text;
using Trag.Domain.Blame;

namespace Trag.Infrastructure.Git.Parsing;

// Git's blame --porcelain output: for every line a header with the commit's id and the line's number (and the group's
// length on a group's first line), then the commit's fields the first time the commit comes, then the line's text after
// a tab. A commit that owns lines under two paths, as moved lines can make it, names its path and the commit before it
// again on each of its groups. With core.quotePath off, Git quotes a path for a quote, a backslash or a control
// character, and the quoting is undone.
internal static class BlamePorcelain
{
    public static FileBlame Parse(byte[] output)
    {
        var commits = new Dictionary<string, BlameCommit>(StringComparer.Ordinal);
        var lines = new List<BlameLine>();
        var at = 0;
        while (at < output.Length)
        {
            var header = Encoding.ASCII.GetString(NextLine(output, ref at)).Split(' ');
            if (header.Length < 3) throw new FormatException("Invalid blame header.");
            var fields = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            ReadOnlySpan<byte> line;
            while ((line = NextLine(output, ref at)) is not [(byte)'\t', ..])
            {
                var space = line.IndexOf((byte)' ');
                fields[Encoding.ASCII.GetString(space < 0 ? line : line[..space])] = space < 0 ? [] : line[(space + 1)..].ToArray();
            }
            var id = header[0];
            var commit = header.Length < 4 && lines.Count > 0 ? lines[^1].Commit
                : !commits.TryGetValue(id, out var known) ? commits[id] = Commit(id, fields)
                : fields.ContainsKey("filename") ? Located(known, fields) : known;
            lines.Add(new(Number(header[2]), Encoding.UTF8.GetString(line[1..].TrimEnd((byte)'\r')), commit));
        }
        return new(lines);
    }

    private static BlameCommit Commit(string id, Dictionary<string, byte[]> fields) => Located(new BlameCommit(id, Encoding.UTF8.GetString(Field(fields, "author")),
        Time(Field(fields, "author-time")), Encoding.UTF8.GetString(Field(fields, "summary")), new(Unquote(Field(fields, "filename")))), fields);

    private static int Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException("Invalid blame line number.");

    private static byte[] Field(Dictionary<string, byte[]> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : throw new FormatException("Incomplete blame commit.");

    // Seconds since 1970, no later than the last date a DateTimeOffset holds.
    private static DateTimeOffset Time(byte[] seconds) =>
        long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(value) : throw new FormatException("Invalid blame time.");

    // The path the commit's lines of this group had, and the commit before it there.
    private static BlameCommit Located(BlameCommit commit, Dictionary<string, byte[]> fields)
    {
        var located = commit with { Path = new(Unquote(fields["filename"])), Previous = null, PreviousPath = null };
        if (!fields.TryGetValue("previous", out var previous)) return located;
        var space = Array.IndexOf(previous, (byte)' ');
        if (space < 0) throw new FormatException("Invalid blame previous commit.");
        return located with { Previous = Encoding.ASCII.GetString(previous, 0, space), PreviousPath = new(Unquote(previous.AsSpan(space + 1))) };
    }

    private static ReadOnlySpan<byte> NextLine(byte[] output, scoped ref int at)
    {
        var end = Array.IndexOf(output, (byte)'\n', at);
        if (end < 0) throw new FormatException("Incomplete blame output.");
        var line = output.AsSpan(at, end - at);
        at = end + 1;
        return line;
    }

    private static byte[] Unquote(ReadOnlySpan<byte> path)
    {
        if (path is not [(byte)'"', .., (byte)'"']) return path.ToArray();
        var bytes = new List<byte>(path.Length);
        for (var index = 1; index < path.Length - 1; index++)
            bytes.Add(path[index] == '\\' ? Escaped(path, ref index) : path[index]);
        return [.. bytes];
    }

    // The byte a backslash stands for with what follows it, as C writes it: a quote or a backslash itself, a control
    // character by its letter, any other byte by three octal digits. The index is left on the escape's last character.
    private static byte Escaped(ReadOnlySpan<byte> path, ref int index) => path[++index] switch
    {
        (byte)'a' => (byte)'\a', (byte)'b' => (byte)'\b', (byte)'t' => (byte)'\t', (byte)'n' => (byte)'\n',
        (byte)'v' => (byte)'\v', (byte)'f' => (byte)'\f', (byte)'r' => (byte)'\r',
        (byte)'"' or (byte)'\\' => path[index],
        var high => Octal(high, path, ref index)
    };

    // The two digits after the first one, both before the closing quote.
    private static byte Octal(byte high, ReadOnlySpan<byte> path, ref int index) => (index += 2) < path.Length - 1
        ? (byte)((high - '0') << 6 | (path[index - 1] - '0') << 3 | (path[index] - '0')) : throw new FormatException("Invalid blame path.");
}
