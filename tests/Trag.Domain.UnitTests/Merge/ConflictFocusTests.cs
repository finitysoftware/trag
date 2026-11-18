using Trag.Domain.Merge;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// Which file a merge or rebase view keeps selected when its lists change.
public sealed class ConflictFocusTests
{
    private static MergeConflict Conflict(string path) => new(Snapshots.Conflict(path), null);
    private static MergeResolved Resolved(string path, bool markers = false) => new(Snapshots.Path(path), Resolution.Edited, markers, null);

    [Fact]
    public void WhenTheSelectedDecidedFileIsStillDecided_ShouldKeepIt()
    {
        var files = TwoConflictsAndADecidedFile();
        Assert.Equal((null, files.Resolved[0]), ConflictFocus.Select(files, null, Snapshots.Path("c.txt")));
    }

    [Fact]
    public void WhenTheSelectedDecidedFileComesBack_ShouldSelectTheConflictAtItsPath()
    {
        var files = TwoConflictsAndADecidedFile();
        Assert.Equal((files.Conflicts[1], null), ConflictFocus.Select(files, null, Snapshots.Path("b.txt")));
    }

    [Fact]
    public void WhenTheSelectedConflictIsStillThere_ShouldSelectIt()
    {
        var files = TwoConflictsAndADecidedFile();
        Assert.Equal((files.Conflicts[1], null), ConflictFocus.Select(files, Snapshots.Path("b.txt"), null));
    }

    [Fact]
    public void WhenTheSelectedConflictIsGone_ShouldSelectTheFirstConflict()
    {
        var files = TwoConflictsAndADecidedFile();
        Assert.Equal((files.Conflicts[0], null), ConflictFocus.Select(files, Snapshots.Path("gone.txt"), null));
    }

    [Fact]
    public void WhenNothingIsSelected_ShouldSelectTheFirstConflict()
    {
        var files = TwoConflictsAndADecidedFile();
        Assert.Equal((files.Conflicts[0], null), ConflictFocus.Select(files, null, null));
    }

    [Fact]
    public void WhenNoFileIsLeft_ShouldSelectNothing()
    {
        Assert.Equal((null, null), ConflictFocus.Select(MergeFiles.Empty, Snapshots.Path("a.txt"), null));
    }

    // With no conflict left and no decided file kept, the first file staged with markers is what Finish waits for.
    [Fact]
    public void WhenOnlyDecidedFilesAreLeftAndSomeHaveMarkers_ShouldSelectTheFirstOfThose()
    {
        var files = new MergeFiles([], [Resolved("clean.txt"), Resolved("marked.txt", markers: true), Resolved("also.txt", markers: true)]);
        Assert.Equal((null, files.Resolved[1]), ConflictFocus.Select(files, null, Snapshots.Path("gone.txt")));
        Assert.Same(files.Resolved[1], ConflictFocus.WithMarkers(files));
    }

    [Fact]
    public void WhenNoDecidedFileHasMarkers_ShouldPointAtNone()
    {
        Assert.Null(ConflictFocus.WithMarkers(new MergeFiles([], [Resolved("clean.txt")])));
    }

    private static MergeFiles TwoConflictsAndADecidedFile() => new([Conflict("a.txt"), Conflict("b.txt")], [Resolved("c.txt")]);
}
