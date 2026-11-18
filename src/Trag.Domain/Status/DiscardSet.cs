using Trag.Domain.Paths;

namespace Trag.Domain.Status;

// What a discard of the chosen unstaged files acts on: conflicts are left to the merge view, and a path counts once
// however many rows chose it. Tracked files return to the index; untracked ones are deleted, and so are new files only
// intended to be added, which the index holds no content of.
public sealed record DiscardSet(IReadOnlyList<FileStatus> Files)
{
    public static DiscardSet From(IEnumerable<FileStatus> files) =>
        new(files.Where(file => !file.IsConflict).DistinctBy(file => file.Path.Key).ToArray());

    public bool IsEmpty => Files.Count == 0;
    public GitPath[] Paths => Files.Select(file => file.Path).ToArray();
    public int UntrackedCount => Files.Count(file => file.IsUntracked || file.IsIntentToAdd);
    public int TrackedCount => Files.Count - UntrackedCount;
}
