using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// When the shown diff has to be read again: the watcher's paths touch its file (either name of a rename), its status
// changed, or the read may have changed anything; and when two reads of a diff show the same thing.
public sealed class SnapshotChangeTests
{
    // A file named by its path, as the blame view names it: touched when the watcher saw it change, or when anything may
    // have changed.
    [Fact]
    public void WhenAPathIsAskedAbout_ShouldBeTouchedOnlyByItsOwnChangeOrByAnything()
    {
        Assert.Equal((true, false, true), (SnapshotChange.Of(["src/a b.cs"]).Touches("src/a b.cs"), SnapshotChange.Of(["src/other.cs"]).Touches("src/a b.cs"),
            SnapshotChange.Everything.Touches("src/a b.cs")));
    }

    [Fact]
    public void WhenOnlyAnotherFileChanged_ShouldNotRereadTheShownFile()
    {
        var (shown, same) = ShownAndUnchanged();
        Assert.False(SnapshotChange.Of(["src/other.cs"]).Rereads(shown, same));
    }

    [Fact]
    public void WhenTheShownFileIsAmongTheChangedPaths_ShouldRereadIt()
    {
        var (shown, same) = ShownAndUnchanged();
        Assert.True(SnapshotChange.Of(["src/other.cs", "src/a b.cs"]).Rereads(shown, same));
    }

    // A folder the watcher names, moved in, moved away or replaced, holds the shown file; a name that only begins like
    // the folder does not.
    [Fact]
    public void WhenAFolderTheShownFileLiesInChanged_ShouldRereadIt()
    {
        var (shown, same) = ShownAndUnchanged();
        Assert.True(SnapshotChange.Of(["src"]).Rereads(shown, same));
        Assert.Equal((true, false), (SnapshotChange.Of(["src"]).Touches("src/a b.cs"), SnapshotChange.Of(["sr"]).Touches("src/a b.cs")));
    }

    [Fact]
    public void WhenTheShownFilesStatusChanged_ShouldRereadIt()
    {
        var (shown, same) = ShownAndUnchanged();
        Assert.True(SnapshotChange.Of(["src/other.cs"]).Rereads(shown, same with { Index = 'M' }));
    }

    [Fact]
    public void WhenAnythingMayHaveChanged_ShouldRereadTheShownFile()
    {
        var (shown, same) = ShownAndUnchanged();
        Assert.True(SnapshotChange.Everything.Rereads(shown, same));
    }

    [Fact]
    public void WhenTheOldNameOfARenamedShownFileChanged_ShouldRereadIt()
    {
        var (_, same) = ShownAndUnchanged();
        var renamed = same with { OriginalPath = new GitPath(Encoding.UTF8.GetBytes("src/old.cs")) };
        Assert.True(SnapshotChange.Of(["src/old.cs"]).Rereads(renamed, renamed));
    }

    [Fact]
    public void WhenNothingButTheFilesMoved_ShouldHoldToThePaths()
    {
        var before = Before();
        Assert.False(SnapshotChange.Between(before, before with { Files = [] }, ["a.txt"]).IsEverything);
    }

    [Fact]
    public void WhenNoPathsAreKnown_ShouldMeanEverything()
    {
        var before = Before();
        Assert.True(SnapshotChange.Between(before, before, null).IsEverything);
    }

    [Fact]
    public void WhenTheIndexChanged_ShouldMeanEverything()
    {
        var before = Before();
        Assert.True(SnapshotChange.Between(before, before with { IndexFingerprint = "i2" }, ["a.txt"]).IsEverything);
    }

    [Fact]
    public void WhenTheHeadMoved_ShouldMeanEverything()
    {
        var before = Before();
        Assert.True(SnapshotChange.Between(before, before with { Head = Snapshots.Id('9') }, ["a.txt"]).IsEverything);
    }

    [Fact]
    public void WhenTwoReadsShowTheSameLines_ShouldHaveTheSameContent() => Assert.True(Read("+x").HasSameContent(Read("+x")));

    [Fact]
    public void WhenTwoReadsShowDifferentLines_ShouldNotHaveTheSameContent() => Assert.False(Read("+x").HasSameContent(Read("+y")));

    [Fact]
    public void WhenTwoReadsDifferOnlyInWhetherTheyCanPreview_ShouldNotHaveTheSameContent() =>
        Assert.False(Read("+x").HasSameContent(Read("+x") with { CanPreview = true }));

    // The shown file and a later read of it with the same status.
    private static (FileStatus Shown, FileStatus Same) ShownAndUnchanged() => (Snapshots.Modified("src/a b.cs"), Snapshots.Modified("src/a b.cs"));

    private static RepositorySnapshot Before() => Snapshots.Create(files: [Snapshots.Modified("a.txt")]) with { IndexFingerprint = "i1" };

    // A new read of a one-line diff of a.txt each time.
    private static FileDiff Read(string text) => new([new(new GitPath("a.txt"u8.ToArray()), "M", "100644", "100644", "1", "2")], [new('+', text, null, 1)], DiffContentKind.Text, "");
}
