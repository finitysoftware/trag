namespace Trag.Domain.Status;

// How long a merge, rebase or pick may run before it is taken for a hung one. One that may wait for a signing key's
// passphrase, or write out the content of Git LFS files, takes as long as it takes; the user can still cancel it.
public static class OperationTimeLimit
{
    public static TimeSpan? Of(RepositorySnapshot snapshot, TimeSpan limit) =>
        snapshot.HasLfsFiles ? null : CommitSignings.Limit(snapshot.Signing, limit);
}
