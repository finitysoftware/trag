using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

public sealed class StatusDeltaTests
{
    [Fact]
    public void WhenBothListsAreEmpty_ShouldPlanNothing()
    {
        Assert.Empty(Plan([], []));
    }

    [Fact]
    public void WhenAFileAppears_ShouldPlanAnInsertAtItsPlace()
    {
        var a = Snapshots.Modified("a.txt");
        var insert = Assert.Single(Plan([], [a]));
        Assert.Equal(0, insert.Index); Assert.NotNull(insert.File); Assert.False(insert.Replace);
    }

    [Fact]
    public void WhenAFileIsGone_ShouldPlanARemoval()
    {
        var a = Snapshots.Modified("a.txt");
        var remove = Assert.Single(Plan([a], []));
        Assert.Null(remove.File);
    }

    [Fact]
    public void WhenNoFileChanged_ShouldPlanNothing()
    {
        var a = Snapshots.Modified("a.txt"); var b = Snapshots.Modified("b.txt");
        Assert.Empty(Plan([a, b], [a, b]));
    }

    [Fact]
    public void WhenAFilesLettersChange_ShouldPlanAReplace()
    {
        var a = Snapshots.Modified("a.txt");
        var replaced = Assert.Single(Plan([a], [a with { Worktree = 'D' }]));
        Assert.True(replaced.Replace);
    }

    [Fact]
    public void WhenAFileBecomesAConflict_ShouldPlanAnEdit()
    {
        var a = Snapshots.Modified("a.txt");
        Assert.Single(Plan([a], [a with { IsConflict = true }]));
    }

    [Fact]
    public void WhenAFileTurnsOutToBeASubmodule_ShouldPlanAnEdit()
    {
        var a = Snapshots.Modified("a.txt");
        Assert.Single(Plan([a], [a with { IsSubmodule = true }]));
    }

    // A submodule checked out from a terminal opens from its row from then on.
    [Fact]
    public void WhenASubmoduleIsCheckedOut_ShouldPlanAnEdit()
    {
        var sub = new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true);
        Assert.Single(Plan([sub], [sub with { SubmoduleIsCheckedOut = true }]));
    }

    [Fact]
    public void WhenARenamesOriginalPathChanges_ShouldPlanAnEdit()
    {
        var renamed = Snapshots.Renamed("old.txt", "new.txt", 'M');
        Assert.Single(Plan([renamed], [renamed with { OriginalPath = Snapshots.Path("older.txt") }]));
    }

    [Fact]
    public void WhenARenameIsUnchanged_ShouldPlanNothing()
    {
        var renamed = Snapshots.Renamed("old.txt", "new.txt", 'M');
        Assert.Empty(Plan([renamed], [renamed]));
    }

    [Fact]
    public void WhenTheStagedSideIsListed_ShouldTakeItsFilesInPathOrder()
    {
        var staged = Snapshots.Staged("b"); var unstaged = Snapshots.Modified("a"); var both = Snapshots.PartiallyStaged("c");
        Assert.Equal(["b", "c"], StatusDelta.Ordered([both, staged, unstaged], true).Select(file => file.Path.Display));
    }

    [Fact]
    public void WhenTheUnstagedSideIsListed_ShouldTakeItsFilesInPathOrder()
    {
        var staged = Snapshots.Staged("b"); var unstaged = Snapshots.Modified("a"); var both = Snapshots.PartiallyStaged("c");
        Assert.Equal(["a", "c"], StatusDelta.Ordered([both, staged, unstaged], false).Select(file => file.Path.Display));
    }

    private static IReadOnlyList<StatusDelta.Edit> Plan(FileStatus[] before, FileStatus[] after) => StatusDelta.Plan(before, StatusDelta.Ordered(after, false));
}
