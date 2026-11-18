using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views.History;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

public sealed class RevertUiTests
{
    private static readonly string Older = Snapshots.Id('b');

    // The revert of the chosen commit (offered by its menu, CommitMenuTests) asks first, with Revert to confirm.
    [AvaloniaFact]
    public async Task WhenARevertOfTheChosenCommitIsRequested_ShouldAskFirst()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await ChosenAsync(h, model, window, 1);

        model.Revert.RequestRevertCommand.Execute(null); AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("RevertOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("Revert commit " + Older[..7] + "?", overlay.Title);
        Assert.False(window.Descendant<TextBlock>("RevertRefusal")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("ConfirmRevertButton")!.IsEffectivelyEnabled);
        window.Descendant<Button>("CancelRevertButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // Git refusing the revert leaves the question open with Git's reason in it.
    [AvaloniaFact]
    public async Task WhenTheRevertIsRefusedByGit_ShouldShowTheReasonInTheDialog()
    {
        var h = new AppHarness();
        h.Writer.Result = GitWriteResult.Rejected("Git could not revert: a hook refused the commit.");
        await using var model = h.Shell();
        var window = h.Window(model);
        await ChosenAsync(h, model, window, 1);
        model.Revert.RequestRevertCommand.Execute(null); AppHarness.Layout(window);

        await model.Revert.Question.ConfirmCommand.ExecuteAsync(null); AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("RevertOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Git could not revert: a hook refused the commit." && block.IsEffectivelyVisible);
        window.Close();
    }

    // The cherry-pick of the chosen commit (offered by its menu, CommitMenuTests) asks first, with Cherry-pick to confirm.
    [AvaloniaFact]
    public async Task WhenACherryPickOfTheChosenCommitIsRequested_ShouldAskFirst()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await ChosenAsync(h, model, window, 1);

        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("CherryPickOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("Cherry-pick commit " + Older[..7] + "?", overlay.Title);
        Assert.False(window.Descendant<TextBlock>("CherryPickRefusal")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("ConfirmCherryPickButton")!.IsEffectivelyEnabled);
        window.Descendant<Button>("CancelCherryPickButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheChosenCommitIsAMerge_ShouldSayWhyAndOfferNoRevert()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await ChosenAsync(h, model, window, 0);

        model.Revert.RequestRevertCommand.Execute(null); AppHarness.Layout(window);

        var refusal = window.Descendant<TextBlock>("RevertRefusal")!;
        Assert.True(refusal.IsEffectivelyVisible);
        Assert.Equal("Reverting a merge commit is not supported. Choose one of the commits it brought in instead.", refusal.Text);
        Assert.False(window.Descendant<Button>("ConfirmRevertButton")!.IsEffectivelyEnabled);
        window.Close();
    }

    // History with a merge commit on top of an ordinary one, and the commit at the index chosen.
    private static async Task<HistoryView> ChosenAsync(AppHarness h, Trag.ViewModels.Shell.ShellViewModel model, Window window, int index)
    {
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Merge topic", Older, Snapshots.Id('c')), Snapshots.Commit(Older, "Older commit")], false, page);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[index]; await model.History.Details.Completion; AppHarness.Layout(window);
        return window.GetVisualDescendants().OfType<HistoryView>().Single();
    }
}
