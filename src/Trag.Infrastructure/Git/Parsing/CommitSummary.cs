using System.Text;
using Trag.Domain;
using Trag.Domain.History;

namespace Trag.Infrastructure.Git.Parsing;

// What a history row shows of a commit, read the same way for the history page, the commit details and the previews.
// A commit may carry a megabyte in any field, so the row decodes each one only as far as it is shown and clips it.
internal static class CommitSummary
{
    private const int SubjectLimit = 1024;

    public static HistoryCommit Of(ReadOnlySpan<byte> id, ReadOnlySpan<byte> parents, ReadOnlySpan<byte> author,
        ReadOnlySpan<byte> email, ReadOnlySpan<byte> date, ReadOnlySpan<byte> message)
    {
        if (!GitObjectId.IsValid(id)) throw new FormatException("Invalid history object ID.");
        return new(Encoding.ASCII.GetString(id), DisplayText.Clip(author, 256), DisplayText.Clip(email, 256),
            DisplayText.Clip(date, 64), Subject(message)) { Parents = Parents(parents) };
    }

    // Git's subject (%s): the first paragraph after the blank lines the message may start with, each of its lines
    // without the spaces, tabs and carriage return it ends with, joined by one space. Only as much is gathered as the
    // row shows.
    private static string Subject(ReadOnlySpan<byte> message)
    {
        Span<byte> subject = stackalloc byte[SubjectLimit * 4];
        var length = 0;
        foreach (var range in message.Split((byte)'\n'))
        {
            var line = message[range].TrimEnd(" \t\r"u8);
            if (line.IsEmpty)
            {
                if (length > 0) break;
                continue;
            }
            if (length > 0) length = Append(subject, length, " "u8);
            length = Append(subject, length, line);
        }
        return DisplayText.Clip(subject[..length], SubjectLimit);
    }

    private static int Append(Span<byte> to, int at, ReadOnlySpan<byte> bytes)
    {
        var count = Math.Min(bytes.Length, to.Length - at);
        bytes[..count].CopyTo(to[at..]);
        return at + count;
    }

    private static string[] Parents(ReadOnlySpan<byte> text)
    {
        var parents = Encoding.ASCII.GetString(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var parent in parents)
            if (!GitObjectId.IsValid(parent)) throw new FormatException("Invalid parent object ID.");
        return parents;
    }
}
