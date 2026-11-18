using System.Text;
using Trag.Domain;
using Trag.Domain.History;

namespace Trag.Infrastructure.Git.Parsing;

// One filter per history page: it holds the query and how many matches the page still has to skip, so a rejected ASCII
// row costs no string, parent array or view model. The query stays data in this process: never a Git option or regexp.
internal sealed class HistoryRecordFilter(string query, int matchesToSkip)
{
    // The history page's record: the commit's fields with the author as written, then the names Git keeps on it (%D), the
    // author as .mailmap names them, whom the page shows, then the message. A search matches the author either way.
    public const string Format = "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%D%x00%aN%x00%aE%x00%B";
    public const int Fields = 9;

    public int MatchesToSkip { get; private set; } = matchesToSkip;

    public HistoryCommit? Match(ReadOnlySpan<byte> record)
    {
        Span<Range> fields = stackalloc Range[Fields];
        HistoryRecordParser.Fields(record, fields);
        var id = record[fields[0]];
        if (!GitObjectId.IsValid(id)) throw new FormatException("Invalid history object ID.");
        if (query.Length != 0 && !Contains(id, query) && !Contains(record[fields[2]], query) && !Contains(record[fields[3]], query)
            && !Contains(record[fields[6]], query) && !Contains(record[fields[7]], query) && !Contains(record[fields[8]], query)) return null;
        if (MatchesToSkip > 0) { MatchesToSkip--; return null; }
        return CommitSummary.Of(id, record[fields[1]], record[fields[6]], record[fields[7]], record[fields[4]], record[fields[8]])
            with { Refs = CommitRefParser.Parse(record[fields[5]]) };
    }

    private static bool Contains(ReadOnlySpan<byte> text, string query)
    {
        // An ASCII query is found in the bytes of any text: an ordinal comparison that ignores case never takes a
        // character outside ASCII for one inside it.
        if (!Ascii.IsValid(query))
        {
            char[]? rented = null;
            Span<char> chars = text.Length <= 1024 ? stackalloc char[text.Length] : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(text.Length));
            try
            {
                var count = Encoding.UTF8.GetChars(text, chars);
                return chars[..count].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            finally { if (rented is not null) System.Buffers.ArrayPool<char>.Shared.Return(rented, clearArray: true); }
        }
        var upper = (byte)char.ToUpperInvariant(query[0]); var lower = (byte)char.ToLowerInvariant(query[0]);
        var offset = 0;
        while (offset <= text.Length - query.Length)
        {
            var next = text[offset..(text.Length - query.Length + 1)].IndexOfAny(upper, lower);
            if (next < 0) break;
            offset += next;
            if (Ascii.EqualsIgnoreCase(text.Slice(offset, query.Length), query)) return true;
            offset++;
        }
        return false;
    }
}
