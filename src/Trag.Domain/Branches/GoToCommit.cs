using Trag.Domain.Status;

namespace Trag.Domain.Branches;

// Double-click in History as Git sees it: nothing when HEAD is already there, and once the branches are read, the one
// local branch whose tip the commit is, which the dialog offers to switch to; with none or several the user chooses a
// branch there or a detached look. A merge or a rebase in progress, or uncommitted changes, keep every move back: the
// dialog opens blocked, even for that branch, until the merge or rebase is finished or aborted and the changes are
// committed, stashed or discarded.
public static class GoToCommit
{
    public static bool IsAt(RepositorySnapshot snapshot, string commitId) => snapshot.Head == commitId;

    // A merge or rebase comes first: its conflicts and staged result are changes too, and only its view ends them.
    public static GoToBlock Blocker(RepositorySnapshot? snapshot) => snapshot switch
    {
        { Operation: RepositoryOperation.Merge } => GoToBlock.Merge,
        { Operation: RepositoryOperation.Rebase } => GoToBlock.Rebase,
        { HasChanges: true } => GoToBlock.Changes,
        _ => GoToBlock.None
    };

    public static BranchInfo? Tip(BranchCatalog catalog, string commitId) =>
        catalog.Local.Where(branch => branch.Head == commitId).ToArray() is [var tip] ? tip : null;
}
