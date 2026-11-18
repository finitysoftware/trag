namespace Trag.Domain.History;

// A name Git keeps on a commit, shown beside its message so the history says which commits are a branch's tip.
public sealed record CommitRef(string Name, CommitRefKind Kind)
{
    public bool IsHead => Kind == CommitRefKind.DetachedHead;
    public bool IsCurrent => Kind == CommitRefKind.CurrentBranch;
    public bool IsBranch => Kind is CommitRefKind.CurrentBranch or CommitRefKind.Branch;
    public bool IsRemote => Kind == CommitRefKind.RemoteBranch;
    public bool IsTag => Kind == CommitRefKind.Tag;
}
