namespace Trag.Infrastructure.Git.Parsing;

// Git's -z output is a NUL terminated stream: every record ends with a NUL, so output that does not was cut short.
// The records are yielded as they are found and keep every other byte; the terminator check is reached when the
// output is fully enumerated. Tabs and newlines are content here, which is why paths and subjects survive intact.
internal static class NulRecordReader
{
    public static IEnumerable<byte[]> Records(byte[] bytes)
    {
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) continue;
            yield return bytes[start..i];
            start = i + 1;
        }
        if (start != bytes.Length) throw new FormatException("Missing NUL terminator.");
    }
}
