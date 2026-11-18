using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// The untracked files a hard reset writes over: Git puts the commit's files in place without asking, so an untracked file
// on the same path, a file where the commit has a folder and a file in a folder where the commit has a file are lost.
public sealed class OverwrittenUntrackedTests
{
    [Fact]
    public void WhenUntrackedFilesStandWhereTheCommitHasFilesOrFolders_ShouldNameEachOneInTheWay()
    {
        var files = new[]
        {
            Snapshots.Untracked("same.txt"), Snapshots.Untracked("folder"), Snapshots.Untracked("file/inside.txt"),
            Snapshots.Untracked("free.txt"), Snapshots.Untracked("other/inside.txt"), Snapshots.Modified("tracked.txt")
        };

        var overwritten = OverwrittenUntracked.Of(files, [Snapshots.Path("same.txt"), Snapshots.Path("folder/a.txt"), Snapshots.Path("file"), Snapshots.Path("tracked.txt"), Snapshots.Path("oth")]);

        Assert.Equal(["same.txt", "folder", "file/inside.txt"], overwritten.Select(path => path.Display));
    }
}
