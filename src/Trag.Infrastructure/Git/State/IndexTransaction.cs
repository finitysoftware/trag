using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.Git.State;

// Own only our CreateNew lock and uniquely named temporary index. Git still runs
// all configured filters/hooks/signing against its normal repository and worktree.
internal sealed class IndexTransaction : IAsyncDisposable
{
    // The private index's file name in the git directory, which the watcher ignores and the scanner recognises.
    public const string NamePrefix = "trag-write-";
    public const string NamePattern = NamePrefix + "*.index";

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string TemporaryIndex { get; }
    private readonly string index;
    private readonly string lockPath;
    private FileStream? ownedLock;
    private bool published;
    private bool preserveIndex;

    public GitWriteResult KeepForReview(string message, string? commitId = null, string details = "")
    {
        preserveIndex = true;
        return new(GitWriteOutcome.Uncertain, message, details + WriteText.RecoveryIndex(TemporaryIndex), commitId);
    }
    public IndexTransaction(string gitDirectory)
    {
        index = Path.Combine(gitDirectory, "index");
        lockPath = index + ".lock";
        TemporaryIndex = Path.Combine(gitDirectory, NamePrefix + Id + ".index");
        ownedLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous);
    }
    // A lock that is already there belongs to another operation and is never touched: the caller says so and stops.
    // Any other reason the lock cannot be taken is a failure of its own and propagates.
    public static IndexTransaction? TryBegin(string gitDirectory)
    {
        try { return new(gitDirectory); }
        catch (IOException) when (File.Exists(Path.Combine(gitDirectory, "index.lock"))) { return null; }
    }
    public async Task CopyAsync(CancellationToken ct)
    {
        if (!File.Exists(index)) return; // Git creates an empty/unborn index itself.
        await using (var input = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous))
        await using (var output = new FileStream(TemporaryIndex, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        // Git reads a file by content when its entry is not older than the index; with the copy's own time, a file
        // changed within that second would look unchanged in the index Git writes from it.
        File.SetLastWriteTimeUtc(TemporaryIndex, File.GetLastWriteTimeUtc(index));
    }
    // Whether Git left the private index byte for byte as it was copied: a write that changes the index then did nothing,
    // whatever Git's exit code said.
    public async Task<bool> UnchangedAsync(CancellationToken ct)
    {
        if (!File.Exists(index)) return false;
        var copied = await File.ReadAllBytesAsync(index, ct).ConfigureAwait(false);
        return (await File.ReadAllBytesAsync(TemporaryIndex, ct).ConfigureAwait(false)).AsSpan().SequenceEqual(copied);
    }
    public async Task PublishAsync(CancellationToken ct)
    {
        await using (var input = new FileStream(TemporaryIndex, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous))
            await input.CopyToAsync(ownedLock!, ct).ConfigureAwait(false);
        await ownedLock!.FlushAsync(ct).ConfigureAwait(false);
        ownedLock.Flush(flushToDisk: true);
        ct.ThrowIfCancellationRequested();
        await ownedLock.DisposeAsync().ConfigureAwait(false);
        ownedLock = null;
        // The time Git wrote the private index at, for the same reason as the copy's.
        File.SetLastWriteTimeUtc(lockPath, File.GetLastWriteTimeUtc(TemporaryIndex));
        File.Move(lockPath, index, overwrite: true);
        published = true;
    }
    public async ValueTask DisposeAsync()
    {
        if (ownedLock is not null) await ownedLock.DisposeAsync().ConfigureAwait(false);
        // Never delete a lock created by another operation after our atomic rename.
        if (!published) TemporaryFiles.Delete(lockPath);
        TemporaryFiles.Delete(TemporaryIndex + ".lock");
        if (!preserveIndex) TemporaryFiles.Delete(TemporaryIndex);
    }
}
