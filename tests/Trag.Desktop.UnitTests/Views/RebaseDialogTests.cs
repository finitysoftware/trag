using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Dialogs;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The rebase side of the merge dialog in the window: the switch under the header swaps the panes, the title and the
// button, the preview card follows the summary, the button rebases, and rebasing the branch others build on warns first.
public sealed class RebaseDialogTests
{
    [AvaloniaFact]
    public async Task WhenTheRebaseSwitchIsChosen_ShouldShowTheRebasePaneWhosePreviewAndButtonRebase()
    {
        var h = new AppHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = Snapshots.Id('b');
        h.Branches.RebasePreview = _ => new(Snapshots.HeadId, Snapshots.Id('b'), [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 1, 2, [], null, 0);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Descendant<Button>("MergeButton")!.Command!.Execute(null); await model.MergeDialog.OpenCommand.ExecutionTask!; AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("MergeBranchesOverlay")!;
        Assert.Equal("Merge branches", overlay.Title);
        Assert.True(window.Descendant<MergePane>("MergePane")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<RebasePane>("RebasePane")!.IsEffectivelyVisible);
        Assert.Contains("selected", window.Descendant<Button>("ShowMergeButton")!.Classes);

        window.Descendant<Button>("ShowRebaseButton")!.Command!.Execute(null); await model.MergeDialog.ShowRebaseCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.Equal(("Rebase a branch", "rebase"), (overlay.Title, overlay.Icon));
        Assert.Contains("selected", window.Descendant<Button>("ShowRebaseButton")!.Classes);
        Assert.False(window.Descendant<MergePane>("MergePane")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<RebasePane>("RebasePane")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Border>("RebasePreviewCard")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("ConfirmMergeButton")!.IsVisible);

        window.Descendant<ComboBox>("RebaseOntoSelector")!.SelectedItem = model.MergeDialog.Rebase.Draft.OntoOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Rebase.Preview.Completion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("RebasePreviewCard")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Canvas>("RebaseGraph")!.IsEffectivelyVisible);
        Assert.Equal("1 commit will be replayed on top of feature", window.Descendant<TextBlock>("RebaseResultTitle")!.Text);
        Assert.True(window.Descendant<Border>("RebaseLocalOnlyCard")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Border>("RebasePublishedCard")!.IsEffectivelyVisible);
        var confirm = window.Descendant<Button>("ConfirmRebaseButton")!;
        Assert.True(confirm.IsEffectivelyVisible); Assert.True(confirm.IsEffectivelyEnabled);

        confirm.Command!.Execute(null);
        await AppHarness.Until(() => !model.MergeDialog.Rebase.IsRebasing && h.Writer.Requests.Count == 1, "the rebase");
        AppHarness.Layout(window);
        Assert.IsType<RebaseRequest>(h.Writer.Requests[0]);
        Assert.False(overlay.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheBranchOthersBuildOnWouldBeRebased_ShouldWarnWithTheOtherWayRoundAndWhatGitLeavesOut()
    {
        var h = new AppHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = Snapshots.Id('b');
        h.Branches.RebasePreview = _ => new(Snapshots.HeadId, Snapshots.Id('b'), [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 30, 1, [], "origin/main", 32)
        {
            Merges = 2, UpstreamIsDefault = true
        };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        await model.MergeDialog.ShowRebaseCommand.ExecuteAsync(null); await model.Branches.List.Completion;
        model.MergeDialog.Rebase.Draft.Onto = model.MergeDialog.Rebase.Draft.OntoOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Rebase.Preview.Completion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("RebaseMainlineCard")!.IsEffectivelyVisible);
        Assert.Equal("This rewrites main, the branch others build on", window.Descendant<TextBlock>("RebaseMainlineTitle")!.Text);
        var otherWay = window.Descendant<Button>("RebaseOtherWayButton")!;
        Assert.True(otherWay.IsEffectivelyVisible); Assert.True(otherWay.IsEffectivelyEnabled);
        Assert.False(window.Descendant<Border>("RebasePublishedCard")!.IsEffectivelyVisible);
        Assert.Equal("Git leaves out 2 merge commits; conflicts they resolved can come back.", window.Descendant<TextBlock>("RebaseLeftOut")!.Text);
        Assert.True(window.Descendant<TextBlock>("RebaseLeftOut")!.IsEffectivelyVisible);
        window.Close();
    }
}
