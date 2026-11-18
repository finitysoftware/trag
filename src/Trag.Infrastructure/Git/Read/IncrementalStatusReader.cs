using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Read;

// A snapshot updated for the paths the watcher saw change, built from the previous one. Anything Git cannot answer for
// a path alone — an operation in progress, a case-only rename, a signaled name that differs only in case from either
// name of a listed row, a signaled name listed rows lie under, a path that is not its own display text — is a full
// read.
internal sealed class IncrementalStatusReader(PresetGitRunner git, IRepositoryReader full)
    : IIncrementalRepositoryReader
{
    public Task<RepositorySnapshot> ReadPathsAsync(RepositorySnapshot previous, IReadOnlyList<string> paths, CancellationToken ct) =>
        Task.Run(async () =>
        {
            if (await HasOperationAsync(previous, ct).ConfigureAwait(false)) return await full.ReadAsync(previous.Root, ct).ConfigureAwait(false);
            // Paths are matched by their bytes, so a display replacement character never stands for a different file.
            var affected = paths.ToHashSet(StringComparer.Ordinal);
            // Where the file system ignores case, a row with a name (a rename's old name too) that differs from a signaled
            // one only in case is the same file under another name the index holds, and Git answers for the signaled name
            // alone.
            var sameFile = paths.ToHashSet(LocalPaths.Comparer);
            // A signaled name that listed rows lie under was a folder, removed or renamed, and rows are matched by name.
            if (previous.Files.Any(file => file.Paths.Any(path => sameFile.Contains(path.Display) && !affected.Contains(path.Display)
                || Folders(path.Display).Any(sameFile.Contains))))
                return await full.ReadAsync(previous.Root, ct).ConfigureAwait(false);
            // A nested repository's row is its folder's name with a slash.
            var keys = affected.SelectMany(path => new[] { Key(path), Key(path + "/") }).ToHashSet(StringComparer.Ordinal);
            // A rename touched on either side is re-read as a whole; a side Git cannot be told about by name is a full read.
            foreach (var file in previous.Files)
            {
                if (file.OriginalPath is null || !Touches(file, keys)) continue;
                if (!file.Path.IsDisplayable || !file.OriginalPath.IsDisplayable) return await full.ReadAsync(previous.Root, ct).ConfigureAwait(false);
                affected.Add(file.Path.Display);
                affected.Add(file.OriginalPath.Display);
                keys.Add(file.Path.Key);
                keys.Add(file.OriginalPath.Key);
            }
            var files = previous.Files.Where(file => !Touches(file, keys)).ToList();
            foreach (var chunk in PathBatches.Of(affected.Select(Pathspec)))
            {
                ct.ThrowIfCancellationRequested();
                files.AddRange(await WorkingTreeStatus.PathsAsync(git, previous.Root, chunk, ct).ConfigureAwait(false));
            }
            return previous with { Files = files.DistinctBy(file => file.RowKey).ToArray() };
        }, ct);

    private async Task<bool> HasOperationAsync(RepositorySnapshot previous, CancellationToken ct) => previous.Operation != RepositoryOperation.None
        || previous.GitDirectory is { } directory && RepositoryOperationDetector.Detect(directory,
            await RootRefs.ReadAsync(git, previous.Root, directory, previous.CommonDirectory!, ct).ConfigureAwait(false)) != RepositoryOperation.None;

    // The folders a path lies in; a nested repository's row ends with a slash and is its folder's own row.
    private static IEnumerable<string> Folders(string path)
    {
        path = path.TrimEnd('/');
        for (var end = path.IndexOf('/'); end > 0; end = path.IndexOf('/', end + 1)) yield return path[..end];
    }

    // Either name of the file was signaled: a rename is one file with two names.
    private static bool Touches(FileStatus file, HashSet<string> keys) =>
        keys.Contains(file.Path.Key) || file.OriginalPath is not null && keys.Contains(file.OriginalPath.Key);

    // Where the file system ignores case, a name the index holds in other case is the same file, which Git answers for
    // only when asked, so the path is matched without regard to case. Elsewhere such a name is another file, which was
    // not signaled and keeps the row it had, so the path is matched exactly.
    private static string Pathspec(string path) => (LocalPaths.Comparison == StringComparison.Ordinal ? ":(literal)" : ":(literal,icase)") + path;

    private static string Key(string path) => new GitPath(Encoding.UTF8.GetBytes(path)).Key;
}
