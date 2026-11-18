namespace Trag.Domain.Status;

public static class RepositoryOperations
{
    // The operation Git's files show, or Conflict when none is in progress but the index holds conflicted entries.
    public static RepositoryOperation With(RepositoryOperation detected, IEnumerable<FileStatus> files) =>
        detected == RepositoryOperation.None && files.Any(file => file.IsConflict) ? RepositoryOperation.Conflict : detected;

    // The commit whose side Git calls theirs in the operation's conflicts: the merged commit, the commit the rebase stopped
    // at, the picked commit, or for a revert the reverted commit's parent, whose content it brings back. Null outside them.
    public static string? Theirs(RepositoryOperation operation, string? merged, string? stopped, string? pickHead) => operation switch
    {
        RepositoryOperation.Merge => merged,
        RepositoryOperation.Rebase => stopped,
        RepositoryOperation.CherryPick => pickHead,
        RepositoryOperation.Revert => pickHead is null ? null : pickHead + "^",
        _ => null
    };
}
