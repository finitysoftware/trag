using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// The files of a merge, rebase, revert or cherry-pick stop in the two lists its view shows: the conflicts still open,
// and every file whose content is decided — the conflicts already resolved and the files Git merged on its own (staged,
// never in conflict).
// A file resolved with markers still in it comes first, because Finish and Continue wait for it. A revert or a
// cherry-pick stopped at its conflicts lists both the same way. Conflicts left by something else (a stash applied over
// changes) are listed too; only those operations have a resolved list.
public sealed record MergeFiles(IReadOnlyList<MergeConflict> Conflicts, IReadOnlyList<MergeResolved> Resolved)
{
    public static readonly MergeFiles Empty = new([], []);

    public int Total => Conflicts.Count + Resolved.Count;
    public IReadOnlyList<MergeResolved> WithMarkers => Resolved.Where(file => file.HasMarkers).ToArray();
    public bool HasConflicts => Conflicts.Count != 0;
    public bool HasMarkers => Resolved.Any(file => file.HasMarkers);
    // Finish and Continue wait for every conflict and for every file staged with markers; the writer checks again.
    public bool IsBlocked => HasConflicts || HasMarkers;

    public static MergeFiles From(RepositorySnapshot? snapshot)
    {
        var resolving = snapshot?.Operation is RepositoryOperation.Merge or RepositoryOperation.Rebase or RepositoryOperation.Revert or RepositoryOperation.CherryPick;
        if (snapshot is null || !snapshot.Conflicts.Any() && !resolving) return Empty;
        var blocks = snapshot.Resolutions.Open.ToDictionary(open => open.Path.Key, open => open.Blocks, StringComparer.Ordinal);
        var conflicts = snapshot.Conflicts.OrderBy(file => file.Path, GitPath.ByBytes)
            .Select(file => new MergeConflict(file, blocks.GetValueOrDefault(file.Path.Key))).ToArray();
        if (!resolving) return new(conflicts, []);
        var staged = snapshot.Files.Where(file => file.IsStaged).ToDictionary(file => file.Path.Key, StringComparer.Ordinal);
        var decided = snapshot.Resolutions.Resolved.Select(path => path.Path.Key).ToHashSet(StringComparer.Ordinal);
        var open = snapshot.Conflicts.Select(file => file.Path.Key).ToHashSet(StringComparer.Ordinal);
        var resolved = snapshot.Resolutions.Resolved.Where(path => !open.Contains(path.Path.Key))
            .Select(path => new MergeResolved(path.Path, path.How, path.HasMarkers, staged.GetValueOrDefault(path.Path.Key), path.Reopens))
            .Concat(staged.Values.Where(file => !decided.Contains(file.Path.Key))
                .Select(file => new MergeResolved(file.Path, Resolution.Automatic, false, file)))
            .OrderByDescending(file => file.HasMarkers).ThenBy(file => file.Path, GitPath.ByBytes).ToArray();
        return new(conflicts, resolved);
    }
}
