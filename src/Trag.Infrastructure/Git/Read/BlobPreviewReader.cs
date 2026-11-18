using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The first bounded chunk of a file's content, from the object database or from the working file. Nothing here reads
// more than the caller asked for, which is what keeps a preview of a huge file cheap.
internal sealed class BlobPreviewReader(PresetGitRunner git)
{
    public async Task<byte[]> BlobAsync(string root, string id, int limit, CancellationToken ct) =>
        (await git.RunAsync(root, ["cat-file", "blob", id], ct, prefix: limit).ConfigureAwait(false)).StandardOutput;

    public async Task<byte[]> FileAsync(string path, int limit, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var buffer = new byte[(int)Math.Min(stream.Length, limit)];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
        }
        return buffer[..length];
    }
}
