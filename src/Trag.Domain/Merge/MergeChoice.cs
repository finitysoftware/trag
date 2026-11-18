using Trag.Domain.Branches;

namespace Trag.Domain.Merge;

// A merge as the dialog chose it: the branch whose commits come in, the local branch they go into, and the branch that was
// checked out when the choice was made. Into the current branch it is one merge; into another one Trag switches there
// first, and back afterwards unless the user stays. A fast-forward is Git's default; NoFastForward asks for a merge commit
// anyway, and the message is that commit's. PushAfter sends the target to its upstream once the merge succeeded.
public sealed record MergeChoice(BranchInfo? From, BranchInfo? Into, string? Current)
{
    public bool NoFastForward { get; init; }
    public bool StayOnTarget { get; init; }
    public bool PushAfter { get; init; }
    public string Message { get; init; } = "";

    // Two different branches; the same one on both sides merges nothing.
    public bool IsComplete => From is not null && Into is not null && From.Ref != Into.Ref;
    public bool SwitchesBranch => Into is not null && Into.Name != Current;
    // Only a local branch can take the other side; a remote one has to become a local branch first.
    public bool CanSwap => From is { IsRemote: false } && Into is not null;
    // Merging a branch into the current one and rebasing the current one onto it are two answers to the same wish, so the
    // rebase side takes that branch; a merge into another branch has nothing to hand over.
    public BranchInfo? RebaseOnto => Into is not null && !SwitchesBranch ? From : null;
    public bool CreatesMergeCommit(MergePreview preview) => preview.IsRead && !preview.IsUpToDate && (NoFastForward || !preview.CanFastForward);
}
