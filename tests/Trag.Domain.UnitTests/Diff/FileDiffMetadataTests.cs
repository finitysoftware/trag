using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Diff;

public sealed class FileDiffMetadataTests
{
    // A submodule is a commit of another repository, on either side of the change.
    [Fact]
    public void WhenEitherSideIsASubmodule_ShouldBeASubmodule()
    {
        static FileDiffMetadata File(string oldMode, string newMode) => new(Snapshots.Path("lib"), "M", oldMode, newMode, Snapshots.Id('1'), Snapshots.Id('2'));
        Assert.True(File("160000", "160000").IsSubmodule);
        Assert.True(File("100644", "160000").IsSubmodule);
        Assert.False(File("100644", "100755").IsSubmodule);
    }

    private static FileDiffMetadata File(string status, GitPath path) => new(path, status, "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    // A commit's file is blamed at that commit, so the commit has to keep it, by a name that reaches Git as text.
    [Fact]
    public void WhenACommitsFileIsLookedUp_ShouldBlameItOnlyWhereTheCommitKeepsIt()
    {
        Assert.True(File("M", Snapshots.Path("a.txt")).CanBlame);
        Assert.False(File("D", Snapshots.Path("a.txt")).CanBlame);
        Assert.False(File("M", new GitPath([0x63, 0xE8, 0x2E, 0x74])).CanBlame);
    }

    // A commit that leaves a submodule's entry keeps a commit of another repository there, not lines: Git finds no file to
    // blame. A submodule the commit replaced by a file leaves a file.
    [Fact]
    public void WhenACommitsFileIsAGitlink_ShouldNotOfferBlame()
    {
        Assert.False((File("M", Snapshots.Path("sub")) with { OldMode = "160000", NewMode = "160000" }).CanBlame);
        Assert.False((File("A", Snapshots.Path("sub")) with { OldMode = "000000", NewMode = "160000" }).CanBlame);
        Assert.True((File("T", Snapshots.Path("sub")) with { OldMode = "160000", NewMode = "100644" }).CanBlame);
    }

    // The version a commit leaves its file with, which reaches Git by the file's name as text: a deleted file has none, and
    // a submodule's entry is a commit, not a file.
    [Fact]
    public void WhenACommitsFileIsTaken_ShouldHaveAVersionOnlyWhereTheCommitLeavesAFileNamedAsText()
    {
        Assert.True(File("M", Snapshots.Path("a.txt")).HasVersion);
        Assert.False((File("D", Snapshots.Path("a.txt")) with { NewMode = "000000", NewObject = new string('0', 40) }).HasVersion);
        Assert.False((File("M", Snapshots.Path("lib")) with { OldMode = "160000", NewMode = "160000" }).HasVersion);
        Assert.False(File("M", new GitPath([0x63, 0xE8, 0x2E, 0x74])).HasVersion);
    }

    // A file's history is asked of Git by its name as text.
    [Fact]
    public void WhenACommitsFileHistoryIsAskedFor_ShouldOfferItOnlyForAFileNamedAsText()
    {
        Assert.True(File("D", Snapshots.Path("a.txt")).HasHistory);
        Assert.False(File("M", new GitPath([0x63, 0xE8, 0x2E, 0x74])).HasHistory);
    }

    // Undoing what a commit did to a file names the file to Git as text, under both names of a rename; a submodule's entry
    // is a commit, not lines to undo. An added or a deleted file is undone like any other.
    [Fact]
    public void WhenACommitsChangesToAFileAreUndone_ShouldOfferItOnlyForAFileNamedAsText()
    {
        var latin = new GitPath([0x63, 0xE8, 0x2E, 0x74]);
        Assert.True(File("M", Snapshots.Path("a.txt")).CanRevert);
        Assert.True((File("D", Snapshots.Path("a.txt")) with { NewMode = "000000", NewObject = new string('0', 40) }).CanRevert);
        Assert.True((File("R", Snapshots.Path("b.txt")) with { OriginalPath = Snapshots.Path("a.txt") }).CanRevert);
        Assert.False(File("M", latin).CanRevert);
        Assert.False((File("R", Snapshots.Path("b.txt")) with { OriginalPath = latin }).CanRevert);
        Assert.False((File("A", Snapshots.Path("lib")) with { OldMode = "000000", NewMode = "160000" }).CanRevert);
        Assert.False((File("D", Snapshots.Path("lib")) with { OldMode = "160000", NewMode = "000000" }).CanRevert);
    }
}
