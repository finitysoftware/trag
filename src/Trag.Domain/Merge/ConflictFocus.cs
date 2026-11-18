using Trag.Domain.Paths;

namespace Trag.Domain.Merge;

// Which file of a merge or rebase stop a view keeps selected when its lists change. A decided file stays selected while
// it is still decided; otherwise the conflict at the path that was selected (a decided file that came back is a conflict
// at its path), else the first conflict; and with nothing selected, the first file staged with markers, since that is
// what Finish or Continue waits for.
public static class ConflictFocus
{
    // The conflict or the decided file to select, by the paths that were selected.
    public static (MergeConflict? Conflict, MergeResolved? Resolved) Select(MergeFiles files, GitPath? conflict, GitPath? resolved) =>
        resolved is not null && files.Resolved.FirstOrDefault(file => file.Path.IsSame(resolved)) is { } kept ? (null, kept)
        : (files.Conflicts.FirstOrDefault(file => file.File.Path.IsSame(conflict ?? resolved)) ?? files.Conflicts.FirstOrDefault()) is { } next ? (next, null)
        : (null, WithMarkers(files));

    public static MergeResolved? WithMarkers(MergeFiles files) => files.WithMarkers.FirstOrDefault();
}
