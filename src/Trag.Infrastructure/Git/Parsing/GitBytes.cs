using System.Text;

namespace Trag.Infrastructure.Git.Parsing;

// Git output reaches the layer as bytes and each caller decoded it its own way. The four ways that actually differ keep
// their own method: strict decoding rejects invalid UTF-8, lenient decoding replaces it, and the terminators differ too.
internal static class GitBytes
{
    public static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n');

    public static string StrictText(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\r', '\n');

    public static string FirstLine(byte[] bytes) => Encoding.UTF8.GetString(bytes).Trim().Split('\n')[0].TrimEnd('\r');

    public static string StrictLine(byte[] bytes)
    {
        var value = new UTF8Encoding(false, true).GetString(bytes);
        // Remove only Git's terminator, preserving whitespace in directory names.
        if (value.EndsWith('\n')) value = value[..^1];
        if (OperatingSystem.IsWindows() && value.EndsWith('\r')) value = value[..^1];
        return value;
    }
}
