using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Trag.Desktop.Dialogs;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The reset question over the window: how far the branch goes back, the three modes with mixed chosen, and a hard reset
// shown for what it is, a red Reset that names the uncommitted work it takes.
public sealed class ResetUiTests
{
    private static readonly string Older = Snapshots.Id('b');

    [AvaloniaFact]
    public async Task WhenAResetIsAsked_ShouldOfferTheModesWithMixedChosenAndMarkAHardResetAsDangerous()
    {
        var h = new AppHarness();
        h.Branches.Distances[Older] = 1;
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.Reset.RequestResetCommand.ExecuteAsync(null); AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("ResetOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("Reset main to " + Older[..7] + "?", overlay.Title);
        Assert.Equal(model.Reset.Question.Text, overlay.Subtitle);
        var (soft, mixed, hard) = (window.Descendant<RadioButton>("ResetSoftOption")!, window.Descendant<RadioButton>("ResetMixedOption")!, window.Descendant<RadioButton>("ResetHardOption")!);
        Assert.Equal((false, true, false), (soft.IsChecked, mixed.IsChecked, hard.IsChecked));
        var confirm = window.Descendant<Button>("ConfirmResetButton")!;
        Assert.True(confirm.IsEffectivelyEnabled); Assert.DoesNotContain("danger", confirm.Classes);
        Assert.False(window.Descendant<TextBlock>("ResetLoss")!.IsEffectivelyVisible);

        Click(window, hard); AppHarness.Layout(window);

        Assert.Equal((false, false, true), (soft.IsChecked, mixed.IsChecked, hard.IsChecked));
        Assert.Contains("danger", confirm.Classes);
        var loss = window.Descendant<TextBlock>("ResetLoss")!;
        Assert.True(loss.IsEffectivelyVisible); Assert.Equal("1 changed file loses its uncommitted changes for good. Untracked files stay.", loss.Text);
        window.Descendant<Button>("CancelResetButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // During a merge the reset waits: the question says it is the merge that has to be finished or aborted first,
    // instead of only keeping Reset back.
    [AvaloniaFact]
    public async Task WhenAMergeIsInProgress_ShouldSayWhyTheResetWaits()
    {
        var h = new AppHarness();
        h.Branches.Distances[Older] = 1;
        h.Reader.Snapshot = Snapshots.Create(operation: Trag.Domain.Status.RepositoryOperation.Merge, files: [Snapshots.Conflict("a.txt")],
            merge: new("incoming", Snapshots.HeadId, "Merge branch 'feature'", "merge", ""));
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion; AppHarness.Layout(window);

        await model.Reset.RequestResetCommand.ExecuteAsync(null); AppHarness.Layout(window);

        Assert.True(window.Descendant<DialogHost>("ResetOverlay")!.IsEffectivelyVisible);
        Assert.Equal("Merge in progress. Finish or abort it before resetting the branch.", model.Reset.Refusal);
        Assert.False(window.Descendant<Button>("ConfirmResetButton")!.IsEffectivelyEnabled);
        window.Close();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(10, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
}
