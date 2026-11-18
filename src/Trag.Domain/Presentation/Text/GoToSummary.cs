using Trag.Domain.Branches;
using Trag.Domain.History;
using Trag.Domain.Naming;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What the go-to dialog says about the commit double-clicked in History: its title, the branch whose tip it is (the
// dialog then only switches there), and while a merge, a rebase or uncommitted changes keep every move back, why and the
// way out of it.
public sealed record GoToSummary(HistoryCommit? Commit, BranchInfo? Tip, RepositorySnapshot? Snapshot)
{
    public static readonly GoToSummary Empty = new(null, null, null);

    public string Title => Tip is { } tip ? "Switch to " + tip.Name : Commit is { } commit ? "Go to " + GitObjectId.Short(commit.Id) : "";
    public string Subtitle => Commit?.Subject ?? "";
    public bool IsTip => Tip is not null;
    // The branch checked out now is named too: switching moves HEAD only, so that branch stays where it is.
    public string TipText => Tip is { } tip ? "This commit is the tip of " + tip.Name + ", so Trag switches to that branch."
        + (Snapshot?.Branch is { } current ? " You're on " + current + " now; it stays where it is." : "") : "";

    private GoToBlock Blocker => GoToCommit.Blocker(Snapshot);
    public bool IsBlocked => Blocker != GoToBlock.None;
    // A merge or rebase is ended in its own view, which the card opens.
    public bool IsBlockedByOperation => Blocker is GoToBlock.Merge or GoToBlock.Rebase;
    public bool IsBlockedByRebase => Blocker == GoToBlock.Rebase;
    public string BlockedTitle => Blocker switch
    {
        GoToBlock.Merge => "Finish or abort the merge first",
        GoToBlock.Rebase => "Finish or abort the rebase first",
        _ => BranchText.ChangesInTheWay
    };
    public string BlockedText => Blocker switch
    {
        GoToBlock.Merge => "A merge of " + MergeMessages.IncomingName(Snapshot!.Merge) + " into " + (Snapshot.Branch ?? "HEAD")
            + " is still in progress. Moving to another commit waits until the merge is finished or aborted.",
        GoToBlock.Rebase => "A rebase of " + (Snapshot!.Rebase?.Branch ?? "HEAD") + " onto " + (Snapshot.Rebase?.OntoText ?? "another commit")
            + " is still in progress" + (Snapshot.Rebase is { Total: > 0 } rebase ? " at commit " + rebase.Step + " of " + rebase.Total : "")
            + ". Moving to another commit waits until the rebase is finished or aborted.",
        GoToBlock.Changes => Counted.Uncommitted(Snapshot!.Files.Count)
            + " Moving to another commit needs a clean working tree, so your edits are not carried along.",
        _ => ""
    };
    public string OperationViewText => Blocker == GoToBlock.Rebase ? "Open rebase view" : "Open merge view";
}
