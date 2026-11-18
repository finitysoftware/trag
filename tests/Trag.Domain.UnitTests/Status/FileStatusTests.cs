using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

public sealed class FileStatusTests
{
    [Fact]
    public void WhenOnlyTheIndexLetterSaysChanged_ShouldBeStagedOnly()
    {
        Assert.True(Snapshots.Staged("a").IsStaged); Assert.False(Snapshots.Staged("a").IsUnstaged);
    }

    [Fact]
    public void WhenOnlyTheWorkingTreeLetterSaysChanged_ShouldBeUnstagedOnly()
    {
        Assert.False(Snapshots.Modified("a").IsStaged); Assert.True(Snapshots.Modified("a").IsUnstaged);
    }

    [Fact]
    public void WhenAFileIsInConflict_ShouldAlwaysBeUnstaged()
    {
        Assert.False(Snapshots.Conflict("c").IsStaged); Assert.True(Snapshots.Conflict("c").IsUnstaged);
    }

    [Fact]
    public void WhenAFileIsUntracked_ShouldSaySo()
    {
        Assert.True(Snapshots.Untracked("u").IsUntracked);
    }

    [Fact]
    public void WhenATrackedFileIsModified_ShouldNotBeUntracked()
    {
        Assert.False(Snapshots.Modified("m").IsUntracked);
    }

    [Fact]
    public void WhenAFileIsNotRenamed_ShouldAnswerForItsOwnPathOnly()
    {
        Assert.Equal(["m"], Snapshots.Modified("m").Paths.Select(path => path.Display));
    }

    [Fact]
    public void WhenAFileIsRenamed_ShouldAnswerForItsNewPathAndItsOldOne()
    {
        Assert.Equal(["new", "old"], Snapshots.Renamed("old", "new").Paths.Select(path => path.Display));
    }

    // The rename is the index's already: nothing is left under the old name, and git add would refuse it.
    [Fact]
    public void WhenARenameInTheIndexIsStaged_ShouldNameOnlyTheNewPath()
    {
        Assert.Equal(["new"], Snapshots.Renamed("old", "new", 'M').StagePaths.Select(path => path.Display));
        Assert.Equal(["copy"], new FileStatus(Snapshots.Path("copy"), 'C', 'M', Snapshots.Path("source")).StagePaths.Select(path => path.Display));
    }

    // The rename is the working tree's: the index still holds the old name, and staging records its deletion.
    [Fact]
    public void WhenARenameInTheWorkingTreeIsStaged_ShouldNameBothPaths()
    {
        Assert.Equal(["new", "old"], new FileStatus(Snapshots.Path("new"), '.', 'R', Snapshots.Path("old")).StagePaths.Select(path => path.Display));
        Assert.Equal(["m"], Snapshots.Modified("m").StagePaths.Select(path => path.Display));
    }

    // A file Git tracks has a history, by a name that reaches Git as text; its blame needs the file there to read.
    [Fact]
    public void WhenAFileIsLookedUp_ShouldSayWhetherItHasAHistoryAndABlame()
    {
        FileStatus[] files = [new(Snapshots.Path("a.txt"), '.', 'M'), new(Snapshots.Path("new.txt"), '?', '?'), new(Snapshots.Path("gone.txt"), '.', 'D'),
            new(Snapshots.Path("removed.txt"), 'D', '.'), new(new GitPath([0x63, 0xE8, 0x2E, 0x74]), '.', 'M')];

        Assert.Equal([true, false, true, true, false], files.Select(file => file.HasHistory));
        Assert.Equal([true, false, false, false, false], files.Select(file => file.CanBlame));
    }

    // Blame in the lists reads the working tree's file. A submodule's entry there is a commit of another repository, not
    // lines, and Git refuses to blame it, as it does a path a conflict left nothing at.
    [Fact]
    public void WhenTheWorkingTreeHoldsNoFile_ShouldNotOfferBlame()
    {
        Assert.False(new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, WorkingTreeHasNoFile: true).CanBlame);
        Assert.False(new FileStatus(Snapshots.Path("sub"), 'A', '.', IsSubmodule: true, WorkingTreeHasNoFile: true).CanBlame);
        Assert.False(new FileStatus(Snapshots.Path("sub"), 'T', '.', IsSubmodule: true, WorkingTreeHasNoFile: true).CanBlame);
        Assert.False(new FileStatus(Snapshots.Path("sub"), '.', 'T', IsSubmodule: true, WorkingTreeHasNoFile: true).CanBlame);
        Assert.False(new FileStatus(Snapshots.Path("p"), 'A', 'U', IsConflict: true, IsSubmodule: true, WorkingTreeHasNoFile: true).CanBlame);
    }

    // A submodule the working tree or the index replaced by a file leaves a file in the working tree, which Git blames.
    [Fact]
    public void WhenASubmoduleIsReplacedByAFile_ShouldOfferBlame()
    {
        Assert.True(new FileStatus(Snapshots.Path("sub"), '.', 'T', IsSubmodule: true).CanBlame);
        Assert.True(new FileStatus(Snapshots.Path("sub"), 'T', '.', IsSubmodule: true).CanBlame);
    }
}
