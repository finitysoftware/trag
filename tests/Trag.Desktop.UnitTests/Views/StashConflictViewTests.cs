using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Views.Merge;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// Conflicts a stash left, as the window shows them: the file status banner opens the conflict view with the panels the
// merge view uses, and the sidebar offers that view while the conflicts last.
public sealed class StashConflictViewTests
{
    [AvaloniaFact]
    public async Task WhenAStashLeftConflicts_ShouldOpenTheConflictViewFromTheBannerAndOfferItInTheSidebar()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.Conflict, files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Assert.True(window.Descendant<Button>("ConflictsButton")!.IsEffectivelyVisible);

        var open = window.Descendant<Button>("OpenMergeViewButton")!;
        open.Command!.Execute(open.CommandParameter); AppHarness.Layout(window);

        var view = window.GetVisualDescendants().OfType<StashConflictsView>().Single();
        Assert.True(view.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "1 file is in conflict. A file is done once it is marked as resolved; nothing is finished afterwards.");
        Assert.Contains(view.Descendant<ListBox>("ConflictList")!.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "a.txt");
        window.Close();
    }
}
