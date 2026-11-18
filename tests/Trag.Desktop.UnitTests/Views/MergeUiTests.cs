using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The merge dialog in the window: the toolbar button after Fetch and Ctrl+Shift+M open it, the blocked card offers
// Stash on top of it, the preview card and the conflicts follow the summary, and the button merges.
public sealed class MergeUiTests
{
    private static AppHarness Harness()
    {
        var h = new AppHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = Snapshots.Id('b');
        h.Branches.Preview = (_, _) => new(Snapshots.HeadId, Snapshots.Id('b'), [Snapshots.Commit(Snapshots.Id('b'), "Add login")], 1, 1,
            [Snapshots.Path("src/Auth/LoginService.cs")]);
        return h;
    }

    [AvaloniaFact]
    public async Task WhenTheToolbarMergeButtonIsUsed_ShouldOpenTheDialogThatShowsThePreviewAndMerges()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var button = window.Descendant<Button>("MergeButton")!;
        Assert.True(button.IsEffectivelyEnabled);
        var names = ((StackPanel)button.Parent!).Children.Select(control => control.Name).ToArray();
        Assert.Equal("MergeButton", names[Array.IndexOf(names, "FetchButton") + 1]);
        var overlay = window.Descendant<DialogHost>("MergeBranchesOverlay")!;
        Assert.False(overlay.IsEffectivelyVisible);

        button.Command!.Execute(null); await model.MergeDialog.OpenCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.False(window.Descendant<Border>("MergePreviewCard")!.IsEffectivelyVisible);
        var from = window.Descendant<ComboBox>("MergeFromSelector")!;
        from.SelectedItem = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("MergePreviewCard")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Canvas>("MergeCommitGraph")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Canvas>("FastForwardGraph")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Border>("MergeConflictCard")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Border>("MergeBlockedCard")!.IsEffectivelyVisible);
        Assert.Equal("Merge branch 'feature' into main", window.Descendant<TextBox>("MergeMessageEditor")!.Text);
        var confirm = window.Descendant<Button>("ConfirmMergeButton")!;
        Assert.True(confirm.IsEffectivelyEnabled);

        confirm.Command!.Execute(null);
        await AppHarness.Until(() => !model.MergeDialog.Merge.IsMerging && h.Writer.Requests.Count == 1, "the merge");
        AppHarness.Layout(window);
        Assert.IsType<MergeRequest>(h.Writer.Requests[0]);
        Assert.False(overlay.IsEffectivelyVisible);
        window.Close();
    }

    // Into another branch the dialog says Trag switches there and offers to stay; a target without an upstream is not
    // offered a push, and one with an upstream is.
    [AvaloniaFact]
    public async Task WhenTheTargetIsAnotherBranchWithoutAnUpstream_ShouldShowTheSwitchAndStayButNoPush()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        var draft = model.MergeDialog.Merge.Draft;
        draft.Into = draft.IntoOptions.Single(row => row.Name == "feature");
        draft.From = draft.FromOptions.Single(row => row.Name == "main");
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);

        Assert.True(window.Descendant<Border>("MergeSwitchCard")!.IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible
            && block.Text == "feature is not checked out. Trag switches to feature, merges, then switches back to main.");
        var stay = window.Descendant<CheckBox>("MergeStayCheck")!;
        Assert.True(stay.IsEffectivelyVisible); Assert.Equal("Stay on feature after merging", stay.Content);
        Assert.False(window.Descendant<CheckBox>("MergePushCheck")!.IsEffectivelyVisible);

        model.MergeDialog.Merge.SwapCommand.Execute(null); await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);

        Assert.False(window.Descendant<Border>("MergeSwitchCard")!.IsEffectivelyVisible);
        var push = window.Descendant<CheckBox>("MergePushCheck")!;
        Assert.True(push.IsEffectivelyVisible); Assert.Equal("Push main after merging", push.Content);
        window.Close();
    }

    // The button names both branches; long names are shortened on it, so it and Cancel stay inside the dialog and its
    // caption is not cut.
    [AvaloniaFact]
    public async Task WhenTheBranchNamesAreLong_ShouldKeepTheButtonsInsideTheDialog()
    {
        var (into, from) = ("release/" + new string('i', 70), "feature/" + new string('f', 70));
        var h = new AppHarness();
        h.Branches.Local.Add(into); h.Branches.Local.Add(from);
        h.Branches.Heads[from] = Snapshots.Id('b');
        h.Branches.Preview = (_, _) => new(Snapshots.HeadId, Snapshots.Id('b'), [Snapshots.Commit(Snapshots.Id('b'), "Add login")], 1, 1, []);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        var draft = model.MergeDialog.Merge.Draft;
        draft.Into = draft.IntoOptions.Single(row => row.Name == into);
        draft.From = draft.FromOptions.Single(row => row.Name == from);
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);

        var card = window.Descendant<DialogHost>("MergeBranchesOverlay")!.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Card");
        var (left, right) = (card.TranslatePoint(default, window)!.Value.X, card.TranslatePoint(new Point(card.Bounds.Width, 0), window)!.Value.X);
        foreach (var button in new[] { window.Descendant<Button>("CancelMergeButton")!, window.Descendant<Button>("ConfirmMergeButton")! })
        {
            Assert.InRange(button.TranslatePoint(default, window)!.Value.X, left, right);
            Assert.InRange(button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X, left, right);
        }
        var caption = window.Descendant<Button>("ConfirmMergeButton")!.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.True(caption.TextLayout.WidthIncludingTrailingWhitespace <= caption.Bounds.Width, "The caption is cut instead of shortened.");
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThereAreUncommittedChanges_ShouldShowTheBlockedCardWhoseStashOpensOnTopOfTheDialog()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Focus();
        window.KeyPress(Key.M, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.M, null);
        await model.Branches.List.Completion; AppHarness.Layout(window);
        Assert.True(model.MergeDialog.Merge.Draft.IsOpen);
        model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("MergeBlockedCard")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("ConfirmMergeButton")!.IsEffectivelyEnabled);

        window.Descendant<Button>("MergeStashButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(model.Stash.Draft.IsOpen);
        var panel = (Panel)window.Descendant<DialogHost>("StashOverlay")!.Parent!;
        Assert.True(panel.Children.IndexOf(window.Descendant<DialogHost>("StashOverlay")!) > panel.Children.IndexOf(window.Descendant<DialogHost>("MergeBranchesOverlay")!));
        window.Close();
    }

    // Escape closes the stash opened over the merge dialog and leaves the keys with the merge dialog under it, so the next
    // Escape closes that one too instead of reaching the window behind both.
    [AvaloniaFact]
    public async Task WhenTheStashOverTheMergeDialogIsClosedByEscape_ShouldLeaveTheKeysWithTheMergeDialog()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Focus();
        window.KeyPress(Key.M, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.M, null);
        await model.Branches.List.Completion; AppHarness.Layout(window);
        model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        var stash = window.Descendant<Button>("MergeStashButton")!;
        stash.Focus(); stash.Command!.Execute(null); AppHarness.Layout(window);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);
        Assert.False(model.Stash.Draft.IsOpen);
        Assert.Same(stash, window.FocusManager!.GetFocusedElement());
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);

        Assert.False(model.MergeDialog.Merge.Draft.IsOpen);
        window.Close();
    }

    // A stash made from the merge dialog cleans the tree, and the button that opened it goes with the blocked card; the keys
    // go to the merge dialog itself, so Escape still closes it.
    [AvaloniaFact]
    public async Task WhenAStashIsMadeFromTheMergeDialog_ShouldGiveTheKeysToTheMergeDialog()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Focus();
        window.KeyPress(Key.M, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.M, null);
        await model.Branches.List.Completion; AppHarness.Layout(window);
        model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        var stash = window.Descendant<Button>("MergeStashButton")!;
        stash.Focus(); stash.Command!.Execute(null); AppHarness.Layout(window);
        h.Reader.Snapshot = Snapshots.Create();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await AppHarness.Until(() => !model.Repository.IsWriting && !model.Stash.Draft.IsOpen && !stash.IsEffectivelyVisible, "the stash"); AppHarness.Layout(window);
        var merge = window.Descendant<DialogHost>("MergeBranchesOverlay")!;
        Assert.True(window.FocusManager!.GetFocusedElement() is Visual focused && (focused == merge || merge.IsVisualAncestorOf(focused)), "The keys left the merge dialog.");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);

        Assert.False(model.MergeDialog.Merge.Draft.IsOpen);
        window.Close();
    }

    // The abort question takes the place of Finish merge, and Git's reason for refusing the abort shows inside it.
    [AvaloniaFact]
    public async Task WhenTheAbortQuestionIsOpenAndGitRefusesTheAbort_ShouldHideFinishAndShowGitsReasonInTheQuestion()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: "trunk", head: "head", operation: RepositoryOperation.Merge,
            files: [Snapshots.Conflict("a.txt")], merge: new("incoming", "head", "Merge branch 'feature'", "merge", ""));
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Git kept the merge: a file is in use.");
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var finish = window.Descendant<Button>("FinishMergeButton")!;
        Assert.True(finish.IsEffectivelyVisible);

        window.Descendant<Button>("RequestAbortMergeButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(finish.IsEffectivelyVisible);
        await model.Merge.Abort.ConfirmCommand.ExecuteAsync(null); AppHarness.Layout(window);

        Assert.IsType<MergeAbortRequest>(h.Writer.LastRequest);
        Assert.False(finish.IsEffectivelyVisible);
        var question = window.Descendant<Button>("ConfirmAbortMergeButton")!.GetVisualAncestors().OfType<StackPanel>().ElementAt(1);
        Assert.Contains(question.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Git kept the merge: a file is in use." && block.IsEffectivelyVisible);
        window.Close();
    }

    // A conflicted file that is not text, or too large to show, says so where its blocks would be, instead of leaving the
    // panel empty.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAConflictedFileIsBinaryOrTooLarge_ShouldShowTheMessageInsteadOfAnEmptyPanel(bool binary)
    {
        var message = binary ? MergeText.NotText : MergeText.TooLarge;
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: "trunk", head: "head", operation: RepositoryOperation.Merge,
            files: [Snapshots.Conflict("logo.png")], merge: new("incoming", "head", "Merge branch 'feature'", "merge", ""));
        h.Diff.Conflict = _ => ConflictText.Unavailable(message);
        await using var model = h.Shell();
        var window = h.Window(model);

        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion; AppHarness.Layout(window);

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == message && block.IsEffectivelyVisible);
        window.Close();
    }

    // Once the last conflict is resolved nothing is selected, and the panel says what to choose, as the file status does,
    // instead of showing nothing at all; a decided file chosen then takes its place.
    [AvaloniaFact]
    public async Task WhenTheLastConflictIsResolved_ShouldSayWhatToSelectInsteadOfAnEmptyPanel()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: "trunk", head: "head", operation: RepositoryOperation.Merge,
            files: [Snapshots.Conflict("a.txt")], merge: new("incoming", "head", "Merge branch 'feature'", "merge", ""));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var hint = window.GetVisualDescendants().OfType<Trag.Desktop.Views.Merge.ConflictDetailPanel>().Single()
            .GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Select a file to review");
        var whileAConflictIsSelected = hint.IsEffectivelyVisible;

        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Staged("a.txt")], Resolutions = new([new(Snapshots.Path("a.txt"), Resolution.Edited)], []) };
        await model.Session.RefreshAsync(); AppHarness.Layout(window);

        Assert.Null(model.Resolution.SelectedResolved);
        var whileNothingIsSelected = hint.IsEffectivelyVisible;
        model.Resolution.SelectedResolved = model.Resolution.Resolved[0]; AppHarness.Layout(window);

        Assert.Equal((false, true, false), (whileAConflictIsSelected, whileNothingIsSelected, hint.IsEffectivelyVisible));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheBranchPickerOpens_ShouldOfferTheMergeOnEveryRowButTheCurrentOneAndOpenTheDialogFromIt()
    {
        var h = Harness();
        h.Branches.Remote.Add("origin/main");
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        var rows = model.Branches.Rows;
        Assert.All(rows.Local.Concat(rows.Remote), row => Assert.True(row.CanMerge));
        Assert.Equal("Merge origin/main into the current branch", rows.Remote.Single().MergeTip);
        model.Branches.RequestMergeCommand.Execute(rows.Remote.Single());
        await model.Branches.List.Completion; await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("MergeBranchesOverlay")!.IsEffectivelyVisible);
        Assert.Equal("origin/main", model.MergeDialog.Merge.Draft.From!.Name);
        Assert.False(model.MergeDialog.Merge.SwapCommand.CanExecute(null));
        window.Close();
    }
}
