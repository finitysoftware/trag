namespace Trag.Domain.Rebase;

// What a rewrite from one commit of the current branch would take: the commit before it (none for the first commit), that
// commit and every one after it, oldest first, as picks, and how many merge commits lie among them.
public sealed record RewriteRange(string? Base, IReadOnlyList<RewriteStep> Steps, int Merges)
{
    // The commit names a parent that this shallow clone does not have: it has no base here, yet it is not the first commit.
    public bool StartsAtShallowBoundary { get; init; }
}
