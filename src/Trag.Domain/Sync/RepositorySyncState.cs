using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Sync;

public sealed record RepositorySyncState(SyncRemote Remote, IReadOnlyList<SyncBranch> Branches, SyncBranch? Branch,
    int? Ahead, int? Behind, string Message, string? TrackingHead = null)
{
    public RemoteAccountState? Accounts { get; init; }
    // The upstream's tip was once the branch's own tip (an entry of its reflog): the branch was rewritten after it was
    // pushed, by a rebase or an amend, so the commits the upstream has and it lacks are its own old ones, not someone
    // else's. A tip that came in only through a merge or a rebase onto it, since undone, does not count.
    public bool Rewritten { get; init; }

    // The upstream choices to offer: the listed branches, and the chosen one even when the bounded list left it out,
    // so it stays selectable.
    public IReadOnlyList<SyncBranch> Choices => Branch is { } chosen && !Branches.Contains(chosen) ? [.. Branches, chosen] : Branches;

    // A remote Trag cannot drive (its limitation says why) is shown but never synchronized.
    public bool CanSynchronize => Remote.Limitation.Length == 0;
    // The upstream as the user knows it: remote/branch.
    public string UpstreamName => Branch is { } branch ? Remote.Name + "/" + branch.Display : "";
    // Both sides have commits the other lacks.
    public bool HasDiverged => Ahead > 0 && Behind > 0;
    // The chosen branch exists on the remote; a branch a push would create there is no upstream yet.
    public bool HasUpstream => Branch is { IsNew: false };
    // The upstream already has the branch's tip: replacing that commit, by an amend, needs a push that replaces it there.
    public bool UpstreamHasHead => HasUpstream && Ahead == 0;
    // Of the commits a reset or an edit takes off the branch, newest first, those past the ones the upstream lacks are
    // pushed already; none on a branch the upstream does not have yet, or without a count.
    public int PushedAmong(int leaving) => HasUpstream && Ahead is { } ahead && leaving > ahead ? leaving - ahead : 0;
    // Diverged only because the branch itself was rewritten: the upstream's extra commits are its own old ones.
    public bool IsRewritten => HasDiverged && Rewritten;

    // Pull and push move commits between a branch with a commit and a chosen upstream; pull also needs a clean tree, and
    // never runs on a rewritten branch, where it would merge the old commits back in.
    public bool CanTransfer(RepositorySnapshot snapshot) => CanSynchronize && snapshot.Head is not null && Branch is not null;
    public bool CanPull(RepositorySnapshot snapshot) => CanTransfer(snapshot) && !snapshot.HasChanges && !IsRewritten;
    // Why a pull cannot run on the snapshot: changes in the tree, or a branch rewritten after its push; null when it can.
    public string? PullRefusal(RepositorySnapshot snapshot) => CanPull(snapshot) ? null
        : snapshot.HasChanges ? SyncOutcomeText.PullNeedsCleanTree : SyncOutcomeText.PullAfterRewrite;
    // A rewritten branch replaces its upstream; a branch the remote moved on from is pulled instead.
    public bool CanForcePush(RepositorySnapshot snapshot) =>
        CanTransfer(snapshot) && IsRewritten && TrackingHead is not null && HasUpstream;
}
