using System.Security.Cryptography;
using System.Text;

namespace Trag.Infrastructure.Git.State;

// A file Git keeps for an operation in progress, which Git may write as large as it needs. Every byte goes into the
// fingerprint, and the text is the file, or, past the bound, the whole lines before it, so no size makes the repository
// unreadable. A text in another encoding is read as it decodes.
internal static class StateFile
{
    public static async Task<string> ReadAsync(string path, IncrementalHash hash, int bound, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var kept = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var whole = true;
        for (int read; (read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0;)
        {
            hash.AppendData(buffer, 0, read);
            var room = bound - (int)kept.Length;
            if (read > room) whole = false;
            kept.Write(buffer, 0, Math.Min(read, room));
        }
        var bytes = kept.ToArray();
        return Encoding.UTF8.GetString(bytes, 0, whole ? bytes.Length : Array.LastIndexOf(bytes, (byte)'\n') + 1);
    }
}
