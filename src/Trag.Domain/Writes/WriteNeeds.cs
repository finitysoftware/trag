using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The one rule for what each write needs, which the writer asks before it starts and the Git writer again on the fresh
// repository: finishing or aborting a merge needs that merge, continuing, skipping or aborting a rebase needs that rebase,
// finishing or aborting a revert or a cherry-pick that pick, resolving a conflict any of them, saving a sync account nothing, and every
// other write a repository with no operation in progress.
public static class WriteNeeds
{
    public static WriteNeed Of(GitWriteRequest request) => request switch
    {
        CommitRequest { FinishesMerge: true } or MergeAbortRequest => WriteNeed.Merge,
        RebaseContinueRequest or RebaseSkipRequest or RebaseAbortRequest => WriteNeed.Rebase,
        PickContinueRequest or PickAbortRequest => WriteNeed.Pick,
        StageRequest { Resolution: true } or MergeToolRequest or ConflictSideRequest or ConflictBlockRequest or ReopenConflictRequest => WriteNeed.Resolution,
        SyncRequest { Action: SyncAction.SelectAccount } => WriteNeed.Nothing,
        _ => WriteNeed.Writable
    };

    // What the repository has to be before any writer starts: the state the request needs, a merge or rebase Git can still
    // finish or none at all. Then it has to be the repository the user was shown, a commit needs the identity the user was
    // shown, and every other write what it starts from. Null when nothing keeps the write back.
    public static string? Refusal(GitWriteRequest request, RepositorySnapshot fresh)
    {
        if (!StateAllows(request, fresh) || fresh.GitDirectory is null) return WriteText.StateUnsupported;
        if (!SnapshotComparison.MatchesForWrite(request.Expected, fresh)) return WriteText.ChangedOutside;
        if (request is not CommitRequest) return Start(request, fresh);
        if (!fresh.HasCommitIdentity) return WriteText.IdentityMissing;
        if (fresh.Author != request.Expected.Author || fresh.Committer != request.Expected.Committer) return WriteText.IdentityChanged;
        return null;
    }

    // A pick, a merge, a rebase or an edit of commits needs a clean tree, a merge and a rebase a branch with a commit, a new
    // stash a first commit and something it can keep, which no change of a submodule is, staged or not, except a staged
    // typechange between a file and a gitlink, which Git keeps, and the branch checked out is not deleted.
    private static string? Start(GitWriteRequest request, RepositorySnapshot fresh) => request switch
    {
        RevertRequest when fresh.HasChanges => RevertText.NeedsCleanTree,
        CherryPickRequest when fresh.HasChanges => CherryPickText.NeedsCleanTree,
        MergeRequest when fresh.IsDetached || fresh.Head is null => MergeOutcomeText.NeedsBranch,
        MergeRequest when fresh.HasChanges => MergeOutcomeText.NeedsCleanTree,
        RebaseRequest or RewriteRequest when fresh.Branch is null || fresh.Head is null => RebaseText.NeedsBranch,
        RebaseRequest or RewriteRequest when fresh.HasChanges => RebaseText.NeedsCleanTree,
        StashRequest { Action: StashAction.Push } when fresh.Head is null => StashOutcomeText.FirstCommitNeeded,
        StashRequest { Action: StashAction.Push } when fresh.Files.Count == 0 => StashOutcomeText.NothingToStash,
        StashRequest { Action: StashAction.Push } when fresh.Files.All(file => file.IsSubmodule && file.Index != 'T') => StashOutcomeText.OnlySubmodules,
        BranchRequest { Action: BranchAction.Delete, Branch.Name: var name } when name.Trim() == fresh.Branch => BranchOutcomeText.DeleteCurrent(name.Trim()),
        _ => null
    };

    // Whether the repository state is the one a write needs; a write that needs nothing never asks.
    public static bool StateAllows(GitWriteRequest request, RepositorySnapshot snapshot) => Of(request) switch
    {
        WriteNeed.Merge => snapshot.HasSupportedMerge,
        WriteNeed.Rebase => snapshot.HasSupportedRebase,
        WriteNeed.Pick => snapshot.HasSupportedPick,
        WriteNeed.Resolution => snapshot.HasSupportedResolution,
        _ => !snapshot.HasUnsupportedWriteState
    };
}
