using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// What the repository looks like after a merge, read against what it looked like before and the commit that was merged
// in. Every outcome a merge can publish is one of these questions; the verified merge itself only asks them in order.
public sealed record MergeStateCheck(RepositorySnapshot Before, RepositorySnapshot Actual, string IncomingHead)
{
    private bool SameBranchAndIndex => Actual.Branch == Before.Branch && Actual.IndexFingerprint == Before.IndexFingerprint;

    public bool HasPendingMerge => SameBranchAndIndex && Actual.Head == Before.Head
        && Actual.HasSupportedMerge && Actual.Merge!.IncomingHead == IncomingHead;

    // Git ran to the end, refused to commit, and left the merge for the user: conflicts, or a hook that said no.
    public bool LeftPendingMerge(int? exitCode, bool failed) => !failed && exitCode is not (null or 0) && HasPendingMerge;

    public bool IsFastForward(string indexTree, string incomingTree) => SameBranchAndIndex
        && Actual.Head == IncomingHead && Actual.Operation == RepositoryOperation.None && indexTree == incomingTree;

    public bool CouldBeMergeCommit(string indexTree) => SameBranchAndIndex
        && Actual.Head is not null && Actual.Head != Before.Head && indexTree.Length != 0;

    public bool MatchesCommit(string[] commit, string indexTree) => commit.Length == 2
        && commit[0] == Before.Head + " " + IncomingHead && commit[1] == indexTree;

    public bool NeedsMetadataCleanup => Actual.Operation == RepositoryOperation.Merge && Actual.Limitations.Count == 0
        && Actual.Merge is { Limitation.Length: 0 } merge && merge.IncomingHead == IncomingHead && merge.OriginalHead == Before.Head;

    public bool HasFinishedMerge(RepositorySnapshot finished) => finished.Head == Actual.Head
        && finished.Branch == Before.Branch && finished.Operation == RepositoryOperation.None
        && finished.IndexFingerprint == Before.IndexFingerprint;

    public bool IsUnchanged(string indexTree, string originalTree) => SnapshotComparison.SameStateForSync(Before, Actual)
        && Actual.Files.Count == 0 && indexTree == originalTree;
}
