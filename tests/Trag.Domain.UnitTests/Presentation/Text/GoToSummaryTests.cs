using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the go-to dialog says about the commit double-clicked in History: a branch's tip only switches there, any other
// commit is gone to by its id; a merge, a rebase or uncommitted changes keep the move back and say which way out.
public sealed class GoToSummaryTests
{
    private static readonly string Second = Snapshots.Id('b');

    [Fact]
    public void WhenTheCommitIsABranchTip_ShouldSwitchThereAndSaySo()
    {
        var tip = new BranchInfo("old", "refs/heads/old", Second, false);
        var summary = new GoToSummary(Snapshots.Commit(Second, "Older work"), tip, Snapshots.Create());
        Assert.True(summary.IsTip);
        Assert.Equal(("Switch to old", "Older work"), (summary.Title, summary.Subtitle));
        Assert.Equal("This commit is the tip of old, so Trag switches to that branch. You're on main now; it stays where it is.", summary.TipText);
        Assert.False(summary.IsBlocked);
    }

    // Switching moves HEAD only; from a detached HEAD there is no branch that stays.
    [Fact]
    public void WhenTheCommitIsABranchTipSeenFromADetachedHead_ShouldOnlySayItSwitchesThere()
    {
        var tip = new BranchInfo("old", "refs/heads/old", Second, false);
        Assert.Equal("This commit is the tip of old, so Trag switches to that branch.", new GoToSummary(Snapshots.Commit(Second, "Older work"), tip, Snapshots.Create(branch: null)).TipText);
    }

    [Fact]
    public void WhenAMergeIsInProgress_ShouldKeepTheMoveBackAndOfferTheMergeView()
    {
        var summary = new GoToSummary(Snapshots.Commit(Second, "Older work"), null, Snapshots.Merging());
        Assert.True(summary.IsBlockedByOperation); Assert.False(summary.IsBlockedByRebase);
        Assert.Equal(("Finish or abort the merge first", "Open merge view"), (summary.BlockedTitle, summary.OperationViewText));
        Assert.Equal("A merge of origin/dev into main is still in progress. Moving to another commit waits until the merge is finished or aborted.", summary.BlockedText);
    }

    [Fact]
    public void WhenTheCommitIsNoBranchTip_ShouldGoThereByItsId()
    {
        var summary = new GoToSummary(Snapshots.Commit(Second, "Older work"), null, Snapshots.Create());
        Assert.False(summary.IsTip);
        Assert.Equal(("Go to bbbbbbb", ""), (summary.Title, summary.TipText));
    }

    [Fact]
    public void WhenARebaseIsInProgress_ShouldKeepTheMoveBackAndOfferTheRebaseView()
    {
        var summary = new GoToSummary(Snapshots.Commit(Second, "Older work"), null, Snapshots.Rebasing(Snapshots.Rebase(step: 2, total: 3)));
        Assert.True(summary.IsBlocked); Assert.True(summary.IsBlockedByOperation); Assert.True(summary.IsBlockedByRebase);
        Assert.Equal(("Finish or abort the rebase first", "Open rebase view"), (summary.BlockedTitle, summary.OperationViewText));
        Assert.StartsWith("A rebase of feature onto main is still in progress at commit 2 of 3.", summary.BlockedText);
    }

    [Fact]
    public void WhenOnlyChangesKeepTheMoveBack_ShouldOfferNoOperationView()
    {
        var summary = new GoToSummary(Snapshots.Commit(Second, "Older work"), null, Snapshots.Create(files: [Snapshots.Modified("a")]));
        Assert.True(summary.IsBlocked); Assert.False(summary.IsBlockedByOperation); Assert.False(summary.IsBlockedByRebase);
        Assert.Equal("Commit or stash your changes first", summary.BlockedTitle);
        Assert.Equal("1 file has uncommitted changes. Moving to another commit needs a clean working tree, so your edits are not carried along.", summary.BlockedText);
        var two = new GoToSummary(Snapshots.Commit(Second, "Older work"), null, Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Untracked("b")]));
        Assert.StartsWith("2 files have uncommitted changes.", two.BlockedText);
    }

    [Fact]
    public void WhenNoDialogIsOpen_ShouldSayNothing()
    {
        var empty = GoToSummary.Empty;
        Assert.Equal(("", "", false, false), (empty.Title, empty.BlockedText, empty.IsTip, empty.IsBlocked));
    }
}
