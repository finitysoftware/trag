using Trag.Domain.Rebase;
using Trag.TestSupport;
using Trag.ViewModels.Rebase;

namespace Trag.ViewModels.UnitTests.Rebase;

// A row of the dialog says whether its commit is left out or joined into the one above, so the view can dim or indent it.
public sealed class RewriteRowTests
{
    [Fact]
    public void WhenItsActionChanges_ShouldSayWhetherTheCommitIsDroppedOrFolded()
    {
        var row = new RewriteRow(new(Snapshots.Id('b'), "Subject", "Message", RewriteAction.Pick, "Message"));
        List<string> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        Assert.Equal((false, false), (row.IsDropped, row.IsFolded));

        row.Action = RewriteAction.Drop;
        Assert.Equal((true, false), (row.IsDropped, row.IsFolded));
        row.Action = RewriteAction.Fixup;
        Assert.Equal((false, true), (row.IsDropped, row.IsFolded));
        row.Action = RewriteAction.Squash;
        Assert.True(row.IsFolded);

        Assert.Contains(nameof(RewriteRow.IsDropped), changed);
        Assert.Contains(nameof(RewriteRow.IsFolded), changed);
    }
}
