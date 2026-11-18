using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Views.Merge;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// A revert stopped at its conflicts, as the window shows it: the revert view with the panels the merge view uses, Finish
// held until the last conflict is resolved, Abort behind a question, and the sidebar's way back while the revert lasts.
public sealed class RevertConflictViewTests
{
    [AvaloniaFact]
    public async Task WhenARevertStoppedAtConflicts_ShouldShowTheRevertViewWithFinishHeldAndAbortBehindAQuestion()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.Revert, files: [Snapshots.Conflict("a.txt")]) with { PickHead = Snapshots.Id('b') };
        await using var model = h.Shell();
        var window = h.Window(model);

        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        var view = window.GetVisualDescendants().OfType<PickView>().Single();
        Assert.True(view.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "1 file is in conflict. Resolve each, then finish the revert.");
        Assert.Contains(view.Descendant<ListBox>("ConflictList")!.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "a.txt");
        Assert.False(view.Descendant<Button>("FinishPickButton")!.IsEffectivelyEnabled);
        Assert.True(window.Descendant<Button>("PickButton")!.IsEffectivelyVisible);
        Assert.Equal("Revert in progress", view.Descendant<OperationBanner>("PickBanner")!.Title);
        Assert.Equal("1", window.Descendant<Border>("PickConflictCount")!.GetVisualDescendants().OfType<TextBlock>().Single().Text);

        view.Descendant<Button>("RequestAbortPickButton")!.Command!.Execute(null); AppHarness.Layout(window);
        var confirm = view.Descendant<Button>("ConfirmAbortPickButton")!;
        Assert.True(confirm.IsEffectivelyVisible);
        Assert.False(view.Descendant<Button>("FinishPickButton")!.IsEffectivelyVisible);
        view.Descendant<Button>("KeepPickButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(confirm.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // A file resolved with its markers still in it is named over Finish, which waits, as in the merge view.
    [AvaloniaFact]
    public async Task WhenAResolvedFileStillHoldsMarkers_ShouldNameItOverTheHeldFinish()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.Revert, files: [Snapshots.Staged("a.txt")]) with
        {
            PickHead = Snapshots.Id('b'), Resolutions = new([new(Snapshots.Path("a.txt"), Resolution.Edited, HasMarkers: true)], [])
        };
        await using var model = h.Shell();
        var window = h.Window(model);

        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        var view = window.GetVisualDescendants().OfType<PickView>().Single();
        Assert.True(view.Descendant<Border>("MergeMarkersNotice")!.IsEffectivelyVisible);
        Assert.False(view.Descendant<Button>("FinishPickButton")!.IsEffectivelyEnabled);
        window.Close();
    }

    // A resolved file without a text diff, binary or too large, says why beside the lists, as File status does.
    [AvaloniaFact]
    public async Task WhenAResolvedFileHasNoTextDiff_ShouldSayWhyBesideTheLists()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.Revert, files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("image.png")]) with
        {
            PickHead = Snapshots.Id('b')
        };
        h.Diff.Diff = (_, _, _) => new FileDiff([], [], DiffContentKind.Binary, FileDiffText.BinaryDiff);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        model.Resolution.SelectedResolved = model.Resolution.Resolved.Single(row => row.PathText == "image.png");
        await model.WorkingDiff.Diff.Completion; AppHarness.Layout(window);

        var message = model.WorkingDiff.Diff.Document.Message;
        Assert.NotEmpty(message);
        Assert.Contains(window.GetVisualDescendants().OfType<PickView>().Single().GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text == message && block.IsEffectivelyVisible);
        window.Close();
    }

    // A cherry-pick stopped at its conflicts has the same view and sidebar entry, named for the cherry-pick.
    [AvaloniaFact]
    public async Task WhenACherryPickStoppedAtConflicts_ShouldNameTheViewAndTheSidebarEntryForIt()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.CherryPick, files: [Snapshots.Conflict("a.txt")]) with { PickHead = Snapshots.Id('b') };
        await using var model = h.Shell();
        var window = h.Window(model);

        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        var view = window.GetVisualDescendants().OfType<PickView>().Single();
        Assert.Equal("Cherry-pick in progress", view.Descendant<OperationBanner>("PickBanner")!.Title);
        Assert.Contains(window.Descendant<Button>("PickButton")!.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Cherry-pick in progress");
        window.Close();
    }
}
