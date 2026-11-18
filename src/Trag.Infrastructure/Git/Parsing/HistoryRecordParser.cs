namespace Trag.Infrastructure.Git.Parsing;

// git log -z --format=%H%x00%P%x00%an%x00%ae%x00%aI%x00…%B: NUL separated fields and then the message, which keeps
// every NUL-free byte to the end of the record; the caller says how many fields there are. Only the field boundaries are
// decided here, never what they mean.
internal static class HistoryRecordParser
{
    public static void Fields(ReadOnlySpan<byte> record, Span<Range> fields)
    {
        var start = 0;
        for (var i = 0; i < fields.Length - 1; i++)
        {
            var end = record[start..].IndexOf((byte)0);
            if (end < 0) throw new FormatException("Incomplete history record.");
            fields[i] = start..(start + end); start += end + 1;
        }
        fields[^1] = start..record.Length;
    }
}
