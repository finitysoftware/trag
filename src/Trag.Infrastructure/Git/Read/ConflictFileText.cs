using System.Security.Cryptography;
using System.Text;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Read;

// A conflicted working file as the merge view and the conflict writer see it: the whole file up to a bound, its hash,
// its text when it is UTF-8 without NUL bytes, and the length of the markers Git writes in it. A byte order mark stays in the text, so writing the text back gives
// the same bytes. The path must stay inside the repository and reach no link.
internal static class ConflictFileText
{
    private static readonly UTF8Encoding Strict = new(false, true);

    public static async Task<ConflictText> ReadAsync(string root, string relative, int markerSize, CancellationToken ct)
    {
        var path = RepositoryPathGuard.SafePath(root, relative);
        if (RepositoryPathGuard.HasLink(root, path)) return ConflictText.Unavailable(FileDiffText.Link);
        if (!File.Exists(path)) return ConflictText.Unavailable(MergeText.DeletedOnASide);
        // An editor may hold the file open for writing while it is shown.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length > ConflictText.ByteLimit) return ConflictText.Unavailable(MergeText.TooLarge);
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return From(bytes, markerSize);
    }

    public static ConflictText From(byte[] bytes, int markerSize)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0) return ConflictText.Unavailable(MergeText.NotText);
        try { return new(Strict.GetString(bytes), Hash(bytes)) { MarkerSize = markerSize }; }
        catch (DecoderFallbackException) { return ConflictText.Unavailable(MergeText.NotText); }
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // The conflicts working content still holds; null when the file is a link or gone.
    public static async Task<int?> CountMarkersAsync(string root, string relative, int markerSize, CancellationToken ct)
    {
        var path = RepositoryPathGuard.SafePath(root, relative);
        if (RepositoryPathGuard.HasLink(root, path) || !File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        return await CountMarkersAsync(stream, markerSize, ct).ConfigureAwait(false);
    }

    // The conflicts staged or working content still holds, closed or not. Markers are ASCII lines, so they are found in
    // any text Git merges, whatever its encoding and size: read as Latin-1, which takes every byte, unless a byte order
    // mark names UTF-8, UTF-16 or UTF-32.
    public static async Task<int> CountMarkersAsync(Stream content, int markerSize, CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var count = 0;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            if (ConflictDocument.OpensBlock(line, markerSize)) count++;
        return count;
    }
}
