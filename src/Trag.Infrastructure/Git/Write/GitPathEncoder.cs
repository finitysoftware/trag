using Trag.Domain.Paths;

namespace Trag.Infrastructure.Git.Write;

// What --pathspec-from-file=- --pathspec-file-nul reads: every selected path once, as raw bytes, NUL terminated.
internal static class GitPathEncoder
{
    public static byte[] Encode(IReadOnlyList<GitPath> paths)
    {
        using var input = new MemoryStream();
        foreach (var path in paths.DistinctBy(path => path.Key)) { input.Write(path.Bytes); input.WriteByte(0); }
        return input.ToArray();
    }
}
