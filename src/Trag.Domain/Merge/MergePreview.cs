using Trag.Domain.History;
using Trag.Domain.Paths;

namespace Trag.Domain.Merge;

// What merging one branch into another would do, read from Git before anything changes: the two commits it was read for,
// the newest of the commits the target does not have yet and how many there are, how many commits the target has of its
// own, and the files the merge would leave in conflict. Conflicts is null when Git cannot tell.
public sealed record MergePreview(string IntoHead, string FromHead, IReadOnlyList<HistoryCommit> Incoming, int IncomingCount, int OwnCount,
    IReadOnlyList<GitPath>? Conflicts)
{
    // The two histories have no commit in common here, because they started apart or a shallow clone lacks the one they
    // share, and Git refuses to merge them.
    public bool SharesNoCommit { get; init; }

    // What a dialog shows before Git has answered.
    public static readonly MergePreview None = new("", "", [], 0, 0, []);

    public bool IsRead => FromHead.Length != 0;
    // The target already has every commit, so there is nothing to merge.
    public bool IsUpToDate => IsRead && IncomingCount == 0;
    // The target has no commits of its own, so Git can move it forward without a merge commit.
    public bool CanFastForward => IsRead && IncomingCount != 0 && OwnCount == 0;
    public bool HasConflicts => Conflicts is { Count: > 0 };
    public int MoreCount => Math.Max(0, IncomingCount - Incoming.Count);
}
