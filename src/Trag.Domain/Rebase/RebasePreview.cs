using Trag.Domain.History;
using Trag.Domain.Paths;

namespace Trag.Domain.Rebase;

// What rebasing the current branch onto another would do, read from Git before anything changes: the two commits it was
// read for, the newest of the commits Git would replay and how many there are, how many commits the onto branch has
// that the current one lacks, the files changed on both sides since they parted (null when that could not be read), and,
// for a branch with an upstream, how many of its commits the upstream already has. Git replays neither merge commits
// nor commits whose change the onto branch already has; those are counted apart.
public sealed record RebasePreview(string Head, string OntoHead, IReadOnlyList<HistoryCommit> Replayed, int ReplayedCount, int OntoCount,
    IReadOnlyList<GitPath>? BothSides, string? Upstream, int Published)
{
    public static readonly RebasePreview None = new("", "", [], 0, 0, [], null, 0);

    // Merge commits of the branch that the rebase leaves out, flattening the history into one line.
    public int Merges { get; init; }
    // Commits of the branch whose change the onto branch already has; Git skips them.
    public int AlreadyOnto { get; init; }
    // Whether the upstream is its remote's default branch (origin/HEAD); null when that remote names none.
    public bool? UpstreamIsDefault { get; init; }

    public bool IsRead => OntoHead.Length != 0;
    // Every commit the branch has that the onto branch lacks, replayed or not.
    public int OwnCount => ReplayedCount + Merges + AlreadyOnto;
    // The branch already has every commit of the onto branch.
    public bool IsUpToDate => IsRead && OntoCount == 0;
    // The branch has nothing to replay: the rebase only moves it forward.
    public bool OnlyMoves => IsRead && OntoCount != 0 && ReplayedCount == 0;
    public bool HasBothSides => BothSides is { Count: > 0 };
    // Commits the upstream already has get new ids, so the branch will have to replace its remote copy.
    public bool IsPublished => Published > 0;
    public int MoreCount => Math.Max(0, ReplayedCount - Replayed.Count);
}
