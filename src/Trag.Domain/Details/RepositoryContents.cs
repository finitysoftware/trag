namespace Trag.Domain.Details;

// What a repository holds besides its history: its branches, tags and stashes, the submodules its index has, the Git LFS
// files of its current commit, what its Git folder takes on disk, whether a shallow clone cut its history, and the
// worktrees it has besides the main one.
public sealed record RepositoryContents(int LocalBranches, int RemoteBranches, int Tags, int Stashes, int Submodules, int LfsFiles, long GitSize,
    bool IsShallow, int Worktrees)
{
    public static readonly RepositoryContents None = new(0, 0, 0, 0, 0, 0, 0, false, 0);
}
