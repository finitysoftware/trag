using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

public sealed class DiscardSetTests
{
    // Conflicts belong to the merge view, and a path can be chosen twice (both lists, or a repeated row).
    [Fact]
    public void WhenFilesAreChosenToDiscard_ShouldLeaveConflictsAloneAndTakeEachPathOnce()
    {
        var set = DiscardSet.From([Snapshots.Modified("a"), Snapshots.Conflict("c"), Snapshots.Untracked("u"), Snapshots.Modified("a")]);
        Assert.Equal(["a", "u"], set.Paths.Select(path => path.Display));
        Assert.Equal(1, set.TrackedCount); Assert.Equal(1, set.UntrackedCount); Assert.False(set.IsEmpty);
    }

    // git add -N records a new file's path and none of its content: it has nothing to go back to, so it goes from disk as
    // an untracked file does.
    [Fact]
    public void WhenAFileIsIntentToAdd_ShouldCountItWithTheFilesDeletedFromDisk()
    {
        var intended = new FileStatus(Snapshots.Path("new.txt"), '.', 'A');
        var set = DiscardSet.From([Snapshots.Modified("a"), intended]);
        Assert.True(intended.IsIntentToAdd);
        Assert.Equal((1, 1), (set.TrackedCount, set.UntrackedCount));
    }

    [Fact]
    public void WhenOnlyConflictsAreChosen_ShouldDiscardNothing()
    {
        Assert.True(DiscardSet.From([Snapshots.Conflict("c")]).IsEmpty);
    }

    [Fact]
    public void WhenNothingIsChosen_ShouldDiscardNothing()
    {
        Assert.True(DiscardSet.From([]).IsEmpty);
    }
}
