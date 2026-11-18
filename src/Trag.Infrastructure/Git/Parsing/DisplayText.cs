using System.Text;

namespace Trag.Infrastructure.Git.Parsing;

// What reaches a list row: decoded from at most four bytes per character and clipped with an ellipsis, never the whole field.
internal static class DisplayText
{
    public static string Clip(ReadOnlySpan<byte> bytes, int limit)
    {
        var value = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, limit * 4)]);
        return value.Length > limit ? value[..limit] + "…" : value;
    }
}
