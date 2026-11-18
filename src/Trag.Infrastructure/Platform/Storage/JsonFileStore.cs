using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Trag.Infrastructure.Platform.Storage;

// One settings file on disk. It is read whole or not at all, written by replacement rather than in place, and only one
// operation at a time touches it. A crash or a cancellation during a write leaves the previous file, because the
// replacement is the last thing that happens and the temporary file is removed either way. A file larger than the store
// allows is refused before it is read. What a document means, what an empty file stands for and what a failure says to the
// user all stay with the store that owns the file.
internal sealed class JsonFileStore<TDocument>(string path, JsonTypeInfo<TDocument> type, long maximumBytes)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<TDocument?> LoadAsync(CancellationToken ct) => Task.Run(() => LoadBackgroundAsync(ct), ct);

    private async Task<TDocument?> LoadBackgroundAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return default;
            if (new FileInfo(path).Length > maximumBytes) throw new IOException();
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public Task SaveAsync(TDocument document, CancellationToken ct) => Task.Run(() => SaveBackgroundAsync(document, ct), ct);

    // A change made to the file as it is on disk, while no other process changes it, so a second instance of Trag keeps
    // what it saved meanwhile. The lock is a file beside this one, held only for the change.
    public Task<TDocument> UpdateAsync(Func<TDocument?, TDocument> change, CancellationToken ct) => Task.Run(async () =>
    {
        await using var held = await FileLock.AcquireAsync(path + ".lock", ct).ConfigureAwait(false);
        var document = change(await LoadBackgroundAsync(ct).ConfigureAwait(false));
        await SaveBackgroundAsync(document, ct).ConfigureAwait(false);
        return document;
    }, ct);

    private async Task SaveBackgroundAsync(TDocument document, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, type, ct).ConfigureAwait(false);
                // On the disk before the rename, so a power loss cannot leave an empty file in place of the old one.
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TemporaryFiles.Delete(temporary);
            gate.Release();
        }
    }
}
