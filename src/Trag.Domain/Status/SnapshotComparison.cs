namespace Trag.Domain.Status;

// What two reads of one repository must share before a local write or a synchronization goes on; each rule is shared
// by its callers. Infrastructure reads the second snapshot; what to say when a rule fails stays with the caller.
public static class SnapshotComparison
{
    public static bool MatchesForWrite(RepositorySnapshot expected, RepositorySnapshot actual) => expected.Head == actual.Head
        && expected.Branch == actual.Branch && expected.Operation == actual.Operation && expected.Merge == actual.Merge
        && SameRebase(expected.Rebase, actual.Rebase)
        && expected.IndexFingerprint.Length != 0 && expected.IndexFingerprint == actual.IndexFingerprint;

    // Whether a write left the repository as it found it: the same history, index and operation, and the same files
    // changed in the same way.
    public static bool Unchanged(RepositorySnapshot expected, RepositorySnapshot actual)
    {
        if (!MatchesForWrite(expected, actual) || expected.Files.Count != actual.Files.Count) return false;
        var rows = expected.Files.ToDictionary(file => file.RowKey);
        return actual.Files.All(file => rows.TryGetValue(file.RowKey, out var row) && row.HasSameChange(file));
    }

    // Two readings are the same rebase state when Git wrote the same files.
    public static bool SameRebase(RepositoryRebase? expected, RepositoryRebase? actual) => expected?.Fingerprint == actual?.Fingerprint;

    public static bool SameStateForSync(RepositorySnapshot left, RepositorySnapshot right) =>
        left.HasSameHistory(right) && left.IndexFingerprint == right.IndexFingerprint && left.Operation == right.Operation;

    public static bool ReadyForMerge(RepositorySnapshot expected, RepositorySnapshot fresh) =>
        SameStateForSync(expected, fresh) && !fresh.HasUnsupportedState && !fresh.HasChanges
        && expected.Author == fresh.Author && expected.Committer == fresh.Committer;

    // What every synchronization needs before it starts: the repository is still the one the user was shown, it is in
    // a state Git can synchronize at all, and there is a git directory to work in.
    public static bool ReadyForSync(RepositorySnapshot expected, RepositorySnapshot fresh) =>
        SameStateForSync(expected, fresh) && !fresh.HasUnsupportedState && fresh.GitDirectory is not null;
}
