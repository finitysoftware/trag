using Trag.Domain.Writes;

namespace Trag.Domain.Naming;

// Co-authors as GitHub and GitLab read them: a "Co-authored-by: Name <email>" trailer each, in the message's trailer
// block. That block is the last paragraph after the subject when every line in it is a "Key: value" line, as Git's own
// Signed-off-by is. The message keeps its line breaks and whatever it ends with.
public static class CoAuthors
{
    private const string Key = "Co-authored-by";

    public static IReadOnlyList<GitIdentity> Of(string message) => [.. Text.Of(message).Block.Select(Person).OfType<GitIdentity>()];

    public static string With(string message, GitIdentity person)
    {
        if (Of(message).Any(person.IsSamePerson)) return message;
        var text = Text.Of(message);
        var line = Key + ": " + person.Display;
        return text.Block.Count == 0
            ? text.Body + text.NewLine + text.NewLine + line + text.End
            : text.Body + text.NewLine + line + text.End;
    }

    // A co-author added before anything was written leaves a message that is only its trailers, and Git would take the
    // first one for the subject. A subject that only looks like a trailer, or trailers naming no co-author, are the user's.
    public static bool LacksSubject(string message)
    {
        var text = Text.Of(message);
        return string.IsNullOrWhiteSpace(text.BeforeBlock) && text.Block.Any(line => Person(line) is not null);
    }

    // A block left empty goes with the blank line in front of it.
    public static string Without(string message, GitIdentity person)
    {
        var text = Text.Of(message);
        var kept = text.Block.Where(line => Person(line) is not { } other || !other.IsSamePerson(person)).ToArray();
        if (kept.Length == text.Block.Count) return message;
        return kept.Length == 0 ? text.BeforeBlock.TrimEnd() + text.End : text.BeforeBlock + string.Join(text.NewLine, kept) + text.End;
    }

    private static GitIdentity? Person(string line) =>
        line.StartsWith(Key + ":", StringComparison.OrdinalIgnoreCase) ? GitIdentity.Parse(line[(Key.Length + 1)..]) : null;

    private static bool IsTrailer(string line)
    {
        var colon = line.IndexOf(':');
        return colon > 0 && line[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    // The message up to what it ends with, the part before its trailer block and the block's lines (none without one).
    private sealed record Text(string Body, string BeforeBlock, IReadOnlyList<string> Block, string NewLine, string End)
    {
        public static Text Of(string message)
        {
            var body = message.TrimEnd();
            var newLine = message.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = body.Split('\n');
            var gap = Array.FindLastIndex(lines, line => line.Trim().Length == 0);
            var block = lines[(gap + 1)..].Select(line => line.TrimEnd('\r')).ToArray();
            return gap >= 0 && block.All(IsTrailer)
                ? new(body, string.Join('\n', lines[..(gap + 1)]) + "\n", block, newLine, message[body.Length..])
                : new(body, body, [], newLine, message[body.Length..]);
        }
    }
}
