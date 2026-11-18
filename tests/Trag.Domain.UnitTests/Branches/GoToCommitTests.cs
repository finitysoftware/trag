using Trag.Domain.Branches;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Branches;

public sealed class GoToCommitTests
{
    [Fact]
    public void WhenTheCommitIsHead_ShouldSayItIsAlreadyThere()
    {
        Assert.True(GoToCommit.IsAt(Snapshots.Create(), Snapshots.HeadId));
    }

    [Fact]
    public void WhenTheCommitIsAnother_ShouldMove()
    {
        Assert.False(GoToCommit.IsAt(Snapshots.Create(), new string('b', 40)));
    }

    // Changes keep the move back through the blocker, not here.
    [Fact]
    public void WhenTheCommitIsAnotherAndTheWorkingTreeHasChanges_ShouldStillMove()
    {
        Assert.False(GoToCommit.IsAt(Snapshots.Create(files: [Snapshots.Modified("a")]), new string('b', 40)));
    }

    [Fact]
    public void WhenAFileHasUncommittedChanges_ShouldBlockTheMoveUntilTheyAreCommittedOrStashed()
    {
        var changed = Snapshots.Create(files: [Snapshots.Modified("a")]);
        Assert.Equal(GoToBlock.Changes, GoToCommit.Blocker(changed));
    }

    [Fact]
    public void WhenAMergeIsInProgress_ShouldBlockTheMoveUntilItIsFinishedOrAborted()
    {
        var merging = Snapshots.Merging();
        Assert.Equal(GoToBlock.Merge, GoToCommit.Blocker(merging));
    }

    [Fact]
    public void WhenTheWorkingTreeIsCleanWithNoOperationInProgress_ShouldNotBlockTheMove()
    {
        Assert.Equal(GoToBlock.None, GoToCommit.Blocker(Snapshots.Create()));
    }

    [Fact]
    public void WhenNoSnapshotIsRead_ShouldNotBlockTheMove()
    {
        Assert.Equal(GoToBlock.None, GoToCommit.Blocker(null));
    }

    [Fact]
    public void WhenTheCommitIsTheTipOfOneLocalBranch_ShouldGoToThatBranch()
    {
        var target = new string('b', 40);
        var one = TopicAt(target);
        Assert.Equal("topic", GoToCommit.Tip(one, target)?.Name);
    }

    [Fact]
    public void WhenTheCommitIsTheTipOfSeveralLocalBranches_ShouldGoToNoBranch()
    {
        var target = new string('b', 40);
        var one = TopicAt(target);
        var two = BranchCatalog.Create(new([.. one.Local, new("other", "refs/heads/other", target, false)], [], null), Snapshots.Create(branch: null));
        Assert.Null(GoToCommit.Tip(two, target));
    }

    [Fact]
    public void WhenTheCommitIsNoBranchsTip_ShouldGoToNoBranch()
    {
        var one = TopicAt(new string('b', 40));
        Assert.Null(GoToCommit.Tip(one, new string('d', 40)));
    }

    // main checked out at HEAD, and topic at the given commit.
    private static BranchCatalog TopicAt(string target) =>
        BranchCatalog.Create(new([new("main", "refs/heads/main", Snapshots.HeadId, false), new("topic", "refs/heads/topic", target, false)], [], "main"), Snapshots.Create());


}
