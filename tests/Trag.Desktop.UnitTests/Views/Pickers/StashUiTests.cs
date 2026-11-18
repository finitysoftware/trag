using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Desktop.Views.History;
using Trag.Desktop.Views.Pickers;
using Trag.Domain.Diff;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views.Pickers;

public sealed class StashUiTests
{
    private static readonly string HeadId = new('a', 40);
    private static readonly string StashId = new('c', 40);

    [AvaloniaFact]
    public async Task WhenTheTreeHasChanges_ShouldEnableTheToolbarStashThatOpensTheDialog()
    {
        var h = Harness(); var reader = h.Reader;
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var toolbar = window.Descendant<Button>("StashButton")!; var section = window.Descendant<Button>("StashPickerButton")!;
        Assert.False(toolbar.IsEffectivelyEnabled); Assert.False(section.IsEffectivelyVisible);

        reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(toolbar.IsEffectivelyEnabled);
        model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("StashOverlay")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<TextBox>("StashMessageEditor")!.IsFocused);
        Assert.True(window.Descendant<CheckBox>("StashUntrackedBox")!.IsEffectivelyVisible); Assert.True(window.Descendant<Button>("CreateStashButton")!.IsEffectivelyEnabled);
        model.Stash.CancelStashCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(window.Descendant<DialogHost>("StashOverlay")!.IsEffectivelyVisible);
        window.Close();
    }

    // The flyout lists the entry with Apply and Drop, and a row previews it in History.
    [AvaloniaFact]
    public async Task WhenAStashEntryExists_ShouldShowTheSidebarSectionThatListsPreviewsAndAsksBeforeDroppingIt()
    {
        var h = Harness(); var reader = h.Reader; var stashes = h.Stashes;
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var section = window.Descendant<Button>("StashPickerButton")!;
        Assert.False(section.IsEffectivelyVisible);
        await StashDialogOpenedAndCanceledOverAChange(h, model, window);

        stashes.Add("On main: half-done login form");
        reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head, Files = [] };
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion; AppHarness.Layout(window);
        Assert.True(section.IsEffectivelyVisible); Assert.Contains("half-done login form", model.Stash.StashButtonText);
        section.Flyout!.ShowAt(section); AppHarness.Layout(window);
        var picker = section.Flyout is Flyout { Content: StashPicker content } ? content : null;
        Assert.NotNull(picker); await model.Stash.List.Completion; AppHarness.Layout(window);
        var list = picker!.Descendant<ItemsControl>("StashList")!;
        Assert.Equal(1, list.ItemCount);
        Assert.Contains(picker.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("trash"));
        Assert.Contains(picker.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("rowaction"));
        section.Flyout.Hide();
        await model.Stash.PreviewCommand.ExecuteAsync(model.Stash.Rows[0]); AppHarness.Layout(window);
        var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
        Assert.True(history.Descendant<Button>("ApplyStashButton")!.IsEffectivelyVisible); Assert.True(history.Descendant<Button>("DropStashButton")!.IsEffectivelyVisible);
        Assert.False(history.Descendant<Button>("CommitActionsButton")!.IsEffectivelyVisible);
        Assert.Contains("half-done login form", history.Descendant<TextBlock>("StashPreviewTitle")!.Text);
        model.Stash.RequestDropCommand.Execute(model.Stash.SelectedStash); AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("DropStashOverlay")!.IsEffectivelyVisible);
        Assert.Contains("danger", window.Descendant<Button>("ConfirmDropStashButton")!.Classes); Assert.True(window.Descendant<Button>("CancelDropStashButton")!.IsFocused);
        model.Stash.Drop.CancelCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(window.Descendant<DialogHost>("DropStashOverlay")!.IsEffectivelyVisible);
        window.Close();
    }

    // A row's Apply and Drop are one pair with nothing between their buttons, and the short list needs no loader.
    [AvaloniaFact]
    public async Task WhenThePickerListsAnEntry_ShouldSetApplyAndDropSideBySideWithoutALoader()
    {
        var h = new AppHarness(); var stash = h.Stashes.Add("On main: spike");
        h.Reader.Snapshot = Snapshots.Create(stashHead: stash.Id);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        var picker = new StashPicker { DataContext = model.Stash };
        var window = new Window { Content = picker, Width = 400, Height = 300 };
        window.Show(); AppHarness.Layout(window);

        var buttons = picker.GetVisualDescendants().OfType<Button>().ToArray();
        var (apply, drop) = (buttons.Single(button => button.Classes.Contains("rowaction")), buttons.Single(button => button.Classes.Contains("trash")));
        var gap = AppHarness.Gap(window, apply, drop);
        Assert.Equal(0, gap, 0.5);
        Assert.Empty(picker.GetVisualDescendants().OfType<BusyBar>());
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheStashFlyoutOpensOrCloses_ShouldKeepIsPickerOpenInStepBothWays()
    {
        var h = new AppHarness(); var stash = h.Stashes.Add("On main: spike");
        h.Reader.Snapshot = Snapshots.Create(stashHead: stash.Id);
        h.History.Details = id => new(Snapshots.Commit(id, "spike") with { Parents = [HeadId] }, "Body", []);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion; AppHarness.Layout(window);
        var section = window.Descendant<Button>("StashPickerButton")!;
        var reads = h.Stashes.Calls;
        Assert.False(model.Stash.IsPickerOpen);

        section.Flyout!.ShowAt(section); AppHarness.Layout(window); await model.Stash.List.Completion;
        Assert.True(model.Stash.IsPickerOpen); Assert.Equal(reads + 1, h.Stashes.Calls); Assert.True(section.Flyout.IsOpen);
        await model.Stash.PreviewCommand.ExecuteAsync(model.Stash.Rows[0]); AppHarness.Layout(window);
        Assert.False(model.Stash.IsPickerOpen); Assert.False(section.Flyout.IsOpen);

        section.Flyout.ShowAt(section); AppHarness.Layout(window);
        Assert.True(model.Stash.IsPickerOpen);
        section.Flyout.Hide(); AppHarness.Layout(window);
        Assert.False(model.Stash.IsPickerOpen);
        window.Close();
    }

    // An apply that stops at conflicts goes to the file status, where the sidebar offers the Conflicts view that resolves
    // them.
    [AvaloniaFact]
    public async Task WhenApplyLeavesConflicts_ShouldGoToTheFileStatusAndOfferTheConflictsView()
    {
        var h = new AppHarness(); var stash = h.Stashes.Add("On main: spike");
        h.Reader.Snapshot = Snapshots.Create(stashHead: stash.Id);
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Create(operation: Trag.Domain.Status.RepositoryOperation.Conflict, files: [Snapshots.Conflict("a.txt")], stashHead: stash.Id);
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.NeedsResolution, Trag.Domain.Presentation.Text.StashOutcomeText.AppliedWithConflicts("stash@{0}")));
        };
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        model.Navigator.ShowHistoryCommand.Execute(null); AppHarness.Layout(window);

        await model.Stash.ApplyCommand.ExecuteAsync(model.Stash.Rows[0]); AppHarness.Layout(window);

        Assert.Equal(Trag.Domain.Presentation.WorkspaceSection.FileStatus, model.Navigator.Section);
        Assert.True(window.Descendant<Button>("ConflictsButton")!.IsEffectivelyVisible);
        window.Close();
    }

    // Every write succeeding, and one commit with one file in it: enough for the pane the stash preview borrows.
    private static AppHarness Harness()
    {
        var h = new AppHarness();
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        h.History.Pages = (_, page) => new([Snapshots.Commit(HeadId, "Update image")], false, page);
        h.History.Details = id => new(Snapshots.Commit(id, "Update image") with { Parents = [HeadId] }, "Body",
            [new FileDiffMetadata(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'))]);
        h.History.Diff = (_, _, _) => new FileDiff([], [], DiffContentKind.Text, "");
        return h;
    }

    // The toolbar test's steps: a change to stash, and the dialog opened and canceled over it, so the entry arrives
    // where it always did.
    private static async Task StashDialogOpenedAndCanceledOverAChange(AppHarness h, ShellViewModel model, MainWindow window)
    {
        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);
        model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
        model.Stash.CancelStashCommand.Execute(null); AppHarness.Layout(window);
    }
}
