using Trag.Application.Status;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Status;

// One side of the status in Domain's order: a batch of edits reports each edit with the file it replaced or removed,
// between its two batch events.
public sealed class StatusFilesTests
{
    [Fact]
    public void WhenTheListIsMade_ShouldOrderItsFilesByPath()
    {
        var (a, c) = (Snapshots.Modified("a"), Snapshots.Modified("c"));
        var files = new StatusFiles([c, a], fromIndex: false);
        Assert.Equal([a, c], files);
    }

    [Fact]
    public void WhenABatchOfEditsIsApplied_ShouldHoldTheNewFilesAtTheirNewPlaces()
    {
        var (files, next, _) = Watched();

        files.ApplyBatch(StatusDelta.Plan(files.Snapshot(), next));

        Assert.Equal(next.Select(file => file.Path.Display), files.Select(file => file.Path.Display));
        Assert.Equal(1, files.IndexOf(Snapshots.Path("c"))); Assert.Equal(-1, files.IndexOf(Snapshots.Path("a")));
    }

    [Fact]
    public void WhenABatchOfEditsIsApplied_ShouldReportEachEditWithTheFileItReplacedOrRemovedBetweenTheBatchEvents()
    {
        var (files, next, events) = Watched();

        files.ApplyBatch(StatusDelta.Plan(files.Snapshot(), next));

        Assert.Equal("start", events[0]); Assert.Equal("end", events[^1]);
        Assert.Contains("-/a", events); Assert.Contains("b/-", events); Assert.Contains("c/c", events);
    }

    // a and c listed with every event recorded, and the next list: a gone, b new, c deleted in the working tree. An edit
    // is recorded as its file, then the file it replaced or removed ("-" for none).
    private static (StatusFiles Files, StatusFiles Next, List<string> Events) Watched()
    {
        var (a, b, c) = (Snapshots.Modified("a"), Snapshots.Modified("b"), Snapshots.Modified("c"));
        var files = new StatusFiles([c, a], fromIndex: false);
        List<string> events = [];
        files.BatchStarting += () => events.Add("start");
        files.BatchCompleted += () => events.Add("end");
        files.Edited += (edit, old) => events.Add((edit.File?.Path.Display ?? "-") + "/" + (old?.Path.Display ?? "-"));
        return (files, new StatusFiles([b, c with { Worktree = 'D' }], fromIndex: false), events);
    }
}
