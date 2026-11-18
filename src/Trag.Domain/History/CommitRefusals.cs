using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;

namespace Trag.Domain.History;

// What keeps a commit of the history from being cherry-picked, reverted, reset to or edited from, before Git is asked,
// in each operation's words; empty when nothing does.
public static class CommitRefusals
{
    // onBranch: the current branch has the commit already.
    public static string CherryPick(HistoryCommit commit, RepositorySnapshot? snapshot, bool onBranch) =>
        Held(snapshot, HistoryText.CherryPicking) is { Length: > 0 } held ? held
        : commit.Parents.Count > 1 ? CherryPickText.MergeCommit
        : snapshot is { HasChanges: true } ? CherryPickText.NeedsCleanTree
        : onBranch ? CherryPickText.AlreadyOnBranch(GitObjectId.Short(commit.Id), snapshot?.Branch) : "";

    public static string Revert(HistoryCommit commit, RepositorySnapshot? snapshot) =>
        Held(snapshot, HistoryText.Reverting) is { Length: > 0 } held ? held
        : commit.Parents.Count > 1 ? RevertText.MergeCommit : snapshot is { HasChanges: true } ? RevertText.NeedsCleanTree : "";

    // distance: how many commits leave the branch, read when its HEAD was askedOn; null when the branch does not have the
    // commit. A branch that moved since has other commits leaving it than the ones counted.
    public static string Reset(HistoryCommit commit, RepositorySnapshot? snapshot, int? distance, string? askedOn) =>
        Held(snapshot, HistoryText.Resetting) is { Length: > 0 } held ? held
        : snapshot is not { Branch: { } branch } ? ResetText.NeedsBranch
        : snapshot.Head != askedOn ? ResetText.HeadMoved(branch)
        : distance is null ? ResetText.NotOnBranch(GitObjectId.Short(commit.Id), branch)
        : distance == 0 ? ResetText.AlreadyHere(branch, GitObjectId.Short(commit.Id)) : "";

    // range: the commits from this one to the tip; null when the branch does not have it.
    public static string Rewrite(HistoryCommit commit, RepositorySnapshot? snapshot, RewriteRange? range) =>
        Held(snapshot, HistoryText.Editing) is { Length: > 0 } held ? held
        : snapshot is not { Branch: { } branch } ? RewriteText.NeedsBranch
        : range is null ? RewriteText.NotOnBranch(GitObjectId.Short(commit.Id), branch)
        : range.StartsAtShallowBoundary ? RewriteText.ShallowStart(GitObjectId.Short(commit.Id))
        : range.Merges != 0 ? RewriteText.HasMerges
        : snapshot.HasChanges ? RebaseText.NeedsCleanTree : "";

    // What keeps every write back keeps each of these back first, whatever the commit: an operation in progress, which a
    // rebase leaves with no branch checked out and none of which is theirs to end, then a limitation of the repository.
    private static string Held(RepositorySnapshot? snapshot, string action) =>
        snapshot is { Operation: not RepositoryOperation.None and var operation } ? HistoryText.OperationInProgress(operation, action)
        : snapshot?.Limitations.FirstOrDefault() ?? "";
}
