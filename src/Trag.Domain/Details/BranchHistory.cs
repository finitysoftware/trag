namespace Trag.Domain.Details;

// The current branch's history as the repository's details show it: its commits, the commits of every local and remote
// branch together, and when the first and the latest of its commits were written. A branch without commits has no dates.
public sealed record BranchHistory(int Commits, int AllCommits, DateTimeOffset? First, DateTimeOffset? Latest)
{
    public static readonly BranchHistory None = new(0, 0, null, null);
}
