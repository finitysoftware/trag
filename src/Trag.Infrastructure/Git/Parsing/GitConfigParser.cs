using System.Text;

namespace Trag.Infrastructure.Git.Parsing;

// git config --null: the key is terminated by a newline, so a value keeps every newline it contains. A key with no value
// at all (no "=" in the file) has no newline, and is listed without a value, which is not the same as an empty one.
internal static class GitConfigParser
{
    public static IReadOnlyList<(string Key, string? Value)> Parse(byte[] bytes) => NulRecordReader.Records(bytes).Select(record =>
    {
        var separator = Array.IndexOf(record, (byte)'\n');
        if (separator < 0) return (Encoding.UTF8.GetString(record), (string?)null);
        return (Encoding.UTF8.GetString(record.AsSpan(0, separator)), Encoding.UTF8.GetString(record.AsSpan(separator + 1)));
    }).ToArray();
}
