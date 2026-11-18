using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// When the file status offers to replace the remote branch, and what it says: only for a branch that diverged from its
// upstream because it was rewritten, never for one the remote moved on from, a new one or one without a known tip.
// Meanwhile Pull is off, and its button says why.
public sealed class ForcePushSummaryTests
{
    private static readonly SyncBranch Upstream = new("refs/heads/main", "refs/remotes/origin/main");
    private static readonly RepositorySyncState Rewritten = new(new("origin", "https://fetch.example.invalid/origin", "https://publish.example.invalid/origin", "config"),
        [Upstream], Upstream, 7, 5, "Local comparison", Snapshots.Id('e')) { Rewritten = true };

    [Fact]
    public void WhenTheBranchWasRewrittenAfterItWasPushed_ShouldOfferToReplaceTheRemoteBranchAndSayWhatThatMeans()
    {
        var summary = new ForcePushSummary(Rewritten, Snapshots.Create(branch: "main"));
        Assert.True(summary.Shows);
        Assert.Equal("main no longer matches origin/main", summary.Title);
        Assert.Equal("It was rewritten after it was pushed: 7 commits here are new to the remote, and 5 there are no longer part of it. Pushing replaces the remote branch; anyone who already pulled it has to reset it.", summary.Text);
        Assert.Equal("Replace origin/main with main?", summary.ConfirmTitle);
        Assert.StartsWith("Trag replaces it only while it is still at eeeeeee, where your last fetch saw it.", summary.ConfirmText);
    }

    [Fact]
    public void WhenOneCommitDiffersOnEachSide_ShouldCountThemInTheSingular()
    {
        var summary = new ForcePushSummary(Rewritten with { Ahead = 1, Behind = 1 }, Snapshots.Create(branch: "main"));
        Assert.StartsWith("It was rewritten after it was pushed: 1 commit here is new to the remote, and 1 there is no longer part of it.", summary.Text);
    }

    [Fact]
    public void WhenTheRemoteMovedOnFromABranchThatWasNotRewritten_ShouldNotOfferToReplaceIt()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten with { Rewritten = false }, snapshot).Shows);
    }

    [Fact]
    public void WhenTheRemoteHasNoCommitTheBranchLacks_ShouldNotOfferToReplaceIt()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten with { Behind = 0 }, snapshot).Shows);
    }

    [Fact]
    public void WhenTheBranchHasNoCommitTheRemoteLacks_ShouldNotOfferToReplaceIt()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten with { Ahead = 0 }, snapshot).Shows);
    }

    [Fact]
    public void WhenTheRemoteTipIsUnknown_ShouldNotOfferToReplaceIt()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten with { TrackingHead = null }, snapshot).Shows);
    }

    [Fact]
    public void WhenTheBranchIsNewOnTheRemote_ShouldNotOfferToReplaceIt()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten with { Branch = Upstream with { IsNew = true } }, snapshot).Shows);
    }

    [Fact]
    public void WhenHeadIsUnknown_ShouldNotOfferToReplaceTheRemoteBranch()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(new ForcePushSummary(Rewritten, snapshot with { Head = null }).Shows);
    }

    [Fact]
    public void WhenThereIsNoSyncState_ShouldOfferNothing() => Assert.False(ForcePushSummary.Empty.Shows);

    [Fact]
    public void WhenTheBranchWasRewritten_ShouldTurnPullOffAndSayWhyOnItsButton()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.False(Rewritten.CanPull(snapshot));
        var counts = SyncCounts.From(Rewritten, null, DateTimeOffset.UnixEpoch);
        Assert.Equal(("Pull · 5", false), (counts.PullLabel, counts.SuggestsPull));
        Assert.Equal("Pull is off: this branch was rewritten after it was pushed, so pulling would merge its old commits from origin/main back in. "
            + "To replace origin/main, use Force push with lease in File status.", counts.PullTip);
    }

    [Fact]
    public void WhenTheRemoteMovedOnFromABranchThatWasNotRewritten_ShouldSuggestPullingWithTheUsualTip()
    {
        var snapshot = Snapshots.Create(branch: "main");
        Assert.True((Rewritten with { Rewritten = false }).CanPull(snapshot));
        var movedOn = SyncCounts.From(Rewritten with { Rewritten = false }, null, DateTimeOffset.UnixEpoch);
        Assert.Equal((true, SyncCounts.PullTipText), (movedOn.SuggestsPull, movedOn.PullTip));
    }
}
