using Trag.Application.Status;
using System.Collections.Specialized;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Status;

// The list the file panes bind to: a refresh is applied as the smallest set of edits, never as a reset. The plan itself
// belongs to StatusDelta and is tested in Trag.Domain.UnitTests.
public sealed class StatusListTests
{
    [Fact]
    public void WhenADeltaPlanIsApplied_ShouldKeepUnchangedRowsAndNeverResetTheCollection()
    {
        var a = Snapshots.PartiallyStaged("a"); var c = Snapshots.PartiallyStaged("c"); var e = Snapshots.PartiallyStaged("e");
        var files = new StatusFiles([a, c, e], false);
        var list = new StatusList(files);
        var notifications = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => notifications.Add(e.Action);
        FileStatus[] next = [Snapshots.PartiallyStaged("b"), c, Snapshots.PartiallyStaged("d"), Snapshots.PartiallyStaged("e") with { Index = 'A' }];
        files.ApplyBatch(StatusDelta.Plan(files.Snapshot(), next));
        Assert.Equal(["b", "c", "d", "e"], list.Cast<StatusRow>().Select(row => row.PathText));
        Assert.Same(c, ((StatusRow)list[1]).File);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, notifications);
    }

    [Fact]
    public void WhenARowIsAdded_ShouldRefuseItBecauseTheListIsReadOnly()
    {
        var a = Snapshots.Modified("a.txt"); var b = Snapshots.Modified("b.txt");
        var list = new StatusList(new StatusFiles([a, b], false));
        Assert.Throws<NotSupportedException>(() => list.Add(a)); Assert.True(list.IsReadOnly);
    }

    [Fact]
    public void WhenARowIsLookedUp_ShouldFindItByFileAndSide()
    {
        var a = Snapshots.Modified("a.txt"); var b = Snapshots.Modified("b.txt");
        var list = new StatusList(new StatusFiles([a, b], false));
        Assert.Equal(1, list.IndexOf(new StatusRow(b, false))); Assert.Equal(-1, list.IndexOf(new StatusRow(b, true)));
    }
}
