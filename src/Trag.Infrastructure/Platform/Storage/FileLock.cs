using System.Diagnostics;

namespace Trag.Infrastructure.Platform.Storage;

// A lock that instances of Trag share: a file held open without sharing for as long as the caller keeps the stream. The
// system closes it when a process ends, so a crash leaves nothing that blocks. A lock still held after ten seconds fails.
internal static class FileLock
{
    public static async Task<FileStream> AcquireAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (waited.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Delay(20, ct).ConfigureAwait(false); }
        }
    }
}
