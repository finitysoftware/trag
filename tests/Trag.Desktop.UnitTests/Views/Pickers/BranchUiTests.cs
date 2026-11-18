using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Desktop.Views.History;
using Trag.Desktop.Views.Pickers;
using Trag.Desktop.Views.Sync;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views.Pickers;

public sealed class BranchUiTests
{
    [AvaloniaFact]
    public async Task WhenTheBranchRowsReloadWhileTheCreateFormIsOpen_ShouldKeepItsTypedNameAndStartPoint()
    {
        var h = Harness(); var branches = h.Branches;
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("NewBranchNameEditor")!;
        var selector = window.Descendant<ComboBox>("StartPointSelector")!;
        editor.Text = "typed-name";
        selector.SelectedIndex = 1;
        var selectedRef = model.Branches.Draft.SelectedStartPoint!.Ref;
        branches.Local.Add("another");
        await model.Branches.List.ReloadAsync();
        AppHarness.Layout(window);
        Assert.Equal("typed-name", editor.Text);
        Assert.Equal(selectedRef, model.Branches.Draft.SelectedStartPoint!.Ref);
        Assert.Same(model.Branches.Draft.SelectedStartPoint, selector.SelectedItem);
        window.Close();
    }
    private static readonly string HeadId = new('a', 40);

    [AvaloniaFact]
    public async Task WhenARepositoryOpens_ShouldEnableTheBranchDropdownNamingTheCurrentBranch()
    {
        await using var model = Harness().Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        var button = window.Descendant<Button>("BranchButton")!;
        Assert.False(button.IsEffectivelyEnabled);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Assert.True(button.IsEffectivelyEnabled); Assert.NotNull(button.Flyout);
        Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "main");
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenANameIsTypedInTheCreateBranchDialog_ShouldBindItAndEnableCreateUntilCanceled()
    {
        await using var model = Harness().Shell();
        var window = await ShownAndOpened(model);
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("NewBranchNameEditor")!;
        Assert.True(editor.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("ConfirmCreateBranchButton")!.IsEffectivelyEnabled);
        editor.Text = "feature/ui"; AppHarness.Layout(window);
        Assert.Equal("feature/ui", model.Branches.Draft.Name);
        Assert.True(window.Descendant<Button>("ConfirmCreateBranchButton")!.IsEffectivelyEnabled);
        Assert.Equal(2, window.Descendant<ComboBox>("StartPointSelector")!.ItemCount);
        window.Descendant<Button>("CancelCreateBranchButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(editor.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheHistoryShows_ShouldDrawTheBranchGraphToggleItsScopeAndOfferABranchFromTheSelectedCommit()
    {
        await using var model = Harness().Shell();
        var window = await ShownAndOpened(model);
        await TypeANameAndCancelTheCreateDialog(model, window);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
        var cells = history.GetVisualDescendants().OfType<CommitGraphCell>().ToArray();
        Assert.Equal(2, cells.Length);
        Assert.Same(model.History.Page.Graph, cells[0].Graph);
        Assert.Equal(CommitGraphCell.WidthFor(1), cells[0].Bounds.Width);
        var scope = history.Descendant<Button>("HistoryScopeButton")!;
        Assert.True(scope.IsEffectivelyVisible); Assert.DoesNotContain("active", scope.Classes); Assert.True(model.History.HistoryAllBranches);
        scope.Command!.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        Assert.False(model.History.HistoryAllBranches); Assert.Contains("active", scope.Classes); Assert.StartsWith("Current branch", model.History.Page.Message);
        scope.Command!.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        Assert.True(model.History.HistoryAllBranches); Assert.DoesNotContain("active", scope.Classes);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        Assert.True(model.Branches.CreateBranchFromCommitCommand.CanExecute(null));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenABranchDeletionIsRequested_ShouldAskWithADangerRemoveButtonUntilCanceled()
    {
        await using var model = Harness().Shell();
        var window = await ShownAndOpened(model);
        await TypeANameAndCancelTheCreateDialog(model, window);
        await BrowseTheHistory(model, window);
        model.Branches.Delete.Request(model.Branches.Rows.Local.First()); AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("DeleteBranchOverlay")!; Assert.True(overlay.IsEffectivelyVisible);
        var remove = window.Descendant<Button>("ConfirmDeleteBranchButton")!;
        Assert.Contains("danger", remove.Classes); Assert.True(remove.IsEffectivelyEnabled); Assert.Equal("Remove", remove.Content);
        window.Descendant<Button>("CancelDeleteBranchButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        window.Close();
    }

    // A remote branch goes for everyone who uses it: the question is titled for a remote branch, names its remote, and
    // keeps Remove red.
    [AvaloniaFact]
    public async Task WhenARemoteBranchDeletionIsRequested_ShouldAskWithTheRemoteWordingAndADangerRemoveButton()
    {
        var h = Harness(); h.Branches.Remote.Add("origin/topic");
        await using var model = h.Shell();
        var window = await ShownAndOpened(model);

        model.Branches.Delete.Request(model.Branches.Rows.Remote.Single()); AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("DeleteBranchOverlay")!; Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal(("Delete remote branch", "Remove origin/topic from origin."), (overlay.Title, overlay.Subtitle));
        Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible
            && block.Text == "The branch is deleted on origin for everyone who uses it. Local branches that track it keep their commits.");
        Assert.Contains("danger", window.Descendant<Button>("ConfirmDeleteBranchButton")!.Classes);
        window.Close();
    }

    // More branches than the reader shows: the picker says it shows the first 5,000, and says nothing while all are shown.
    [AvaloniaFact]
    public async Task WhenTheBranchListIsCut_ShouldSayItShowsTheFirstFiveThousandBranches()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.ReloadAsync();
        var picker = new BranchPicker { DataContext = model.Branches };
        var window = new Window { Content = picker };
        window.Show(); AppHarness.Layout(window);
        bool CutLineShown() => picker.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == "Showing the first 5,000 branches.");
        var shownWhole = CutLineShown();

        h.Branches.Truncated = true;
        await model.Branches.List.ReloadAsync(); AppHarness.Layout(window);

        Assert.False(shownWhole);
        Assert.True(CutLineShown());
        window.Close();
    }

    // The picker draws the rows it shows, not every branch of the read: with all of them drawn, opening the picker over
    // 1,000 branches took seconds here and over 5,000 more than half a minute, and every filter keystroke drew the matches again.
    [AvaloniaFact]
    public async Task WhenAThousandBranchesAreListed_ShouldVirtualizeAndFilterWithinBudget()
    {
        var h = Harness();
        for (var index = 0; index < 500; index++) { h.Branches.Local.Add($"feature/topic-{index:D3}"); h.Branches.Remote.Add($"origin/feature/topic-{index:D3}"); }
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.ReloadAsync();
        var picker = new BranchPicker { DataContext = model.Branches };
        var window = new Window { Content = picker };
        window.Show(); AppHarness.Layout(window);
        string?[] DrawnRows() => picker.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && button.Classes.Contains("branch") && !button.Classes.Contains("plain"))
            .Select(button => button.GetVisualDescendants().OfType<TextBlock>().First(text => Grid.GetColumn(text) == 1).Text).ToArray();
        var opened = DrawnRows();

        model.Branches.BranchSearch = "topic-49"; AppHarness.Layout(window);
        var filtered = DrawnRows();
        var scroller = picker.Descendant<ItemsControl>("LocalBranchList")!.FindAncestorOfType<ScrollViewer>()!;
        scroller.Offset = new Vector(0, scroller.Extent.Height); AppHarness.Layout(window);

        Assert.InRange(opened.Length, 1, 40);
        Assert.Equal("feature/topic-000", opened[0]);
        Assert.InRange(filtered.Length, 1, 40);
        Assert.Equal("feature/topic-490", filtered[0]);
        Assert.Equal("origin/feature/topic-499", DrawnRows()[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheBranchSearchFiltersOrTheListReloads_ShouldBindThePickerToTheVisibleRows()
    {
        var h = Harness(); var branches = h.Branches;
        branches.Remote.Add("origin/release");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.ReloadAsync();
        var picker = new BranchPicker { DataContext = model.Branches };
        var window = new Window { Content = picker };
        window.Show();
        AppHarness.Layout(window);
        var current = picker.Descendant<Button>("CurrentBranchButton")!;
        var locals = picker.Descendant<ItemsControl>("LocalBranchList")!;

        Assert.True(current.IsVisible);
        Assert.Same(model.Branches.Visible.Local, locals.ItemsSource);
        Assert.Equal(1, locals.ItemCount);
        model.Branches.BranchSearch = "  RELEASE  ";
        AppHarness.Layout(window);
        Assert.False(current.IsVisible);
        Assert.Equal(0, locals.ItemCount);
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "origin/release");

        model.Branches.BranchSearch = "absent";
        AppHarness.Layout(window);
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "No branches match.");

        branches.Local.Add("another");
        await model.Branches.List.ReloadAsync();
        model.Branches.BranchSearch = "";
        AppHarness.Layout(window);
        Assert.True(current.IsVisible);
        Assert.Equal(2, locals.ItemCount);
        Assert.Same(model.Branches.SwitchToBranchCommand, current.Command); Assert.Same(model.Branches.Visible.Current, current.CommandParameter);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheBranchListLoads_ShouldShowTheBusyBarOnTheBottomEdgeWithoutResizingThePicker()
    {
        var h = Harness(); await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var picker = new BranchPicker { DataContext = model.Branches };
        var window = new Window { Content = picker, SizeToContent = SizeToContent.WidthAndHeight }; window.Show(); AppHarness.Layout(window);
        var bar = picker.Descendant<BusyBar>("BranchBusyBar")!; var slot = picker.Descendant<Border>("BranchBusySlot")!;
        // Both "still visible" checks right after a load race the bar's hide timer on the wall clock: with an 80 ms cycle
        // a stall on a busy machine let the timer win, so the bar keeps its shipping one-second cycle here.
        bar.Cycle = TimeSpan.FromSeconds(1);
        // The list follows the repository; a load asked for here stands for the one a moved ref brings.
        await model.Branches.List.ReloadAsync(); AppHarness.Layout(window);
        // A quick load still shows one full animation cycle before the bar goes away.
        Assert.False(model.Branches.List.IsBusy); Assert.True(bar.IsEffectivelyVisible);
        await AppHarness.Until(() => !bar.IsEffectivelyVisible, "the branch busy bar to finish its cycle"); AppHarness.Layout(window);
        var idleHeight = picker.Bounds.Height;
        Assert.Equal(picker.Bounds.Height, slot.Bounds.Bottom); Assert.Equal(2, slot.Bounds.Height);
        h.Branches.Gate.IsClosed = true; var reload = model.Branches.List.ReloadAsync(); AppHarness.Layout(window);
        Assert.True(bar.IsEffectivelyVisible); Assert.Equal(idleHeight, picker.Bounds.Height);
        Assert.Equal(picker.Bounds.Height, slot.Bounds.Bottom); Assert.Equal(2, bar.Bounds.Height);
        h.Branches.Gate.Open(); await reload; AppHarness.Layout(window);
        Assert.True(bar.IsEffectivelyVisible); Assert.Equal(idleHeight, picker.Bounds.Height);
        await AppHarness.Until(() => !bar.IsEffectivelyVisible, "the branch busy bar to hide after one cycle"); AppHarness.Layout(window);
        Assert.Equal(idleHeight, picker.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAHistoryRowIsDoubleClicked_ShouldOpenGoToCommitWithItsBranchNameFocused()
    {
        await using var model = Harness().Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "HistoryList");
        var row = (ListBoxItem)list.ContainerFromIndex(1)!;
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        for (var click = 0; click < 2; click++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
        AppHarness.Layout(window);
        Assert.True(model.GoTo.Target.IsOpen); Assert.True(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        var editor = window.Descendant<TextBox>("GoToBranchNameEditor")!;
        Assert.Equal("initial", editor.Text); Assert.True(editor.IsFocused);
        Assert.True(window.Descendant<Button>("CreateBranchAtCommitButton")!.IsEffectivelyEnabled); Assert.True(window.Descendant<Button>("DetachAtCommitButton")!.IsEffectivelyEnabled);
        Assert.Equal(HeadId, window.GetVisualDescendants().OfType<CommitGraphCell>().First().Head);
        model.GoTo.CancelGoToCommitCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        window.Close();
    }

    // The card replaces the sync selectors and the branch button says so.
    [AvaloniaFact]
    public async Task WhenHeadIsDetachedWithCommitsOfItsOwn_ShouldShowTheCardAndAskToKeepThemBeforeLeaving()
    {
        await using var detached = Harness(detached: true, detachedCommits: 2).Shell();
        var other = new MainWindow { DataContext = detached }; other.Show(); AppHarness.Layout(other);
        await detached.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(other);
        var panel = other.GetVisualDescendants().OfType<SyncPanel>().Single();
        Assert.True(panel.Descendant<Border>("DetachedCard")!.IsEffectivelyVisible); Assert.False(panel.Descendant<Button>("SyncBranchSelector")!.IsEffectivelyVisible);
        Assert.Equal("Detached at " + HeadId[..7], detached.Header.BranchText); Assert.Equal("2 commits are not on a branch", detached.GoTo.DetachedCardText);
        await detached.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        await detached.Branches.SwitchToBranchCommand.ExecuteAsync(detached.Branches.Rows.Local.First(branch => branch.Name == "topic")); AppHarness.Layout(other);
        Assert.True(other.Descendant<DialogHost>("KeepCommitsOverlay")!.IsEffectivelyVisible);
        Assert.Equal("kept-from-" + HeadId[..7], other.Descendant<TextBox>("KeepBranchNameEditor")!.Text);
        Assert.Contains("danger", other.Descendant<Button>("DiscardCommitsButton")!.Classes); Assert.Contains("primary", other.Descendant<Button>("KeepCommitsButton")!.Classes);
        detached.GoTo.CancelKeepCommitsCommand.Execute(null); AppHarness.Layout(other);
        Assert.False(other.Descendant<DialogHost>("KeepCommitsOverlay")!.IsEffectivelyVisible);
        other.Close();
    }

    // The dialog names the branch and says that the checked-out one stays.
    [AvaloniaFact]
    public async Task WhenABranchTipIsDoubleClicked_ShouldOfferOnlyTheSwitchAndWriteItWhenChosen()
    {
        var h = Harness(); h.Branches.Heads["topic"] = Snapshots.Id('b');
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var row = (ListBoxItem)window.Descendant<ListBox>("HistoryList")!.ContainerFromIndex(1)!;
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        for (var click = 0; click < 2; click++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
        // The double-click starts the go-to without waiting; the dialog opens only after its branch read, so wait for the go-to.
        await model.GoTo.GoToSelectedCommitCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.Empty(h.Writer.Requests);
        Assert.True(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        Assert.Equal("This commit is the tip of topic, so Trag switches to that branch. You're on main now; it stays where it is.",
            window.Descendant<TextBlock>("GoToTipText")!.Text);
        var switching = window.Descendant<Button>("SwitchToTipButton")!;
        Assert.True(switching.IsEffectivelyVisible); Assert.Equal("Switch to topic", switching.Content);
        Assert.False(window.Descendant<Button>("CreateBranchAtCommitButton")!.IsEffectivelyVisible);
        switching.Command!.Execute(null); await model.GoTo.SwitchToTipCommand.ExecutionTask!; AppHarness.Layout(window);
        var request = Assert.IsType<BranchRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("topic", BranchAction.Switch), (request.Branch.Name, request.Action));
        Assert.False(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        window.Close();
    }

    // A branch's tip offers the switch alone: the switch has the keyboard, so Enter switches and Escape closes.
    [AvaloniaTheory]
    [InlineData(Key.Enter, PhysicalKey.Enter)]
    [InlineData(Key.Escape, PhysicalKey.Escape)]
    public async Task WhenTheCommitIsATip_ShouldFocusTheSwitchSoEnterAndEscapeWork(Key key, PhysicalKey physical)
    {
        var h = Harness(); h.Branches.Heads["topic"] = Snapshots.Id('b');
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<Button>("SwitchToTipButton")!.IsFocused);

        window.KeyPress(key, RawInputModifiers.None, physical, null); AppHarness.Layout(window);
        if (model.GoTo.SwitchToTipCommand.ExecutionTask is { } switching) await switching; AppHarness.Layout(window);

        Assert.False(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        Assert.Equal(key == Key.Enter ? 1 : 0, h.Writer.Requests.Count);
        window.Close();
    }

    // The question before leaving a detached HEAD's commits keeps them from the keyboard: Enter in the name keeps them.
    [AvaloniaFact]
    public async Task WhenLeavingADetachedHeadWithCommits_ShouldOpenWithTheSuggestedNameAndKeepOnEnter()
    {
        var h = Harness(detached: true, detachedCommits: 2);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.First(branch => branch.Name == "topic")); AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("KeepBranchNameEditor")!;
        Assert.True(editor.IsFocused); Assert.Equal("kept-from-" + HeadId[..7], editor.Text);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await AppHarness.Until(() => h.Writer.Requests.Count == 2, "keep and switch");

        Assert.Equal([BranchAction.Create, BranchAction.Switch], h.Writer.Requests.Cast<BranchRequest>().Select(request => request.Action));
        Assert.Equal("kept-from-" + HeadId[..7], ((BranchRequest)h.Writer.Requests[0]).Branch.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenGoToCommitOpensWithChangesOrDuringAMerge_ShouldShowTheBlockedCardWithItsWayOutUntilTheTreeIsClean()
    {
        var h = Harness();
        h.Reader.Snapshot = Snapshots.Create(branch: "main", head: HeadId, files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("GoToCommitOverlay")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Border>("GoToBlockedCard")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("CreateBranchAtCommitButton")!.IsEffectivelyEnabled); Assert.False(window.Descendant<Button>("DetachAtCommitButton")!.IsEffectivelyEnabled);
        Assert.Same(model.Stash.OpenStashCommand, window.Descendant<Button>("GoToStashButton")!.Command);
        var panel = (Panel)window.Descendant<DialogHost>("StashOverlay")!.Parent!;
        Assert.True(panel.Children.IndexOf(window.Descendant<DialogHost>("StashOverlay")!) > panel.Children.IndexOf(window.Descendant<DialogHost>("GoToCommitOverlay")!));

        h.Reader.Snapshot = Snapshots.Create(branch: "main", head: HeadId);
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.False(window.Descendant<Border>("GoToBlockedCard")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("CreateBranchAtCommitButton")!.IsEffectivelyEnabled);
        model.GoTo.CancelGoToCommitCommand.Execute(null);

        // During a merge the card names the merge and offers the merge view instead of Stash.
        h.Reader.Snapshot = Snapshots.Create(branch: "main", head: HeadId, files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge,
            merge: new(Snapshots.Id('c'), HeadId, "Merge branch 'topic' into main", "fp", ""));
        await model.RefreshCommand.ExecuteAsync(null);
        await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("GoToBlockedCard")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("GoToOperationViewButton")!.IsEffectivelyVisible); Assert.False(window.Descendant<Button>("GoToStashButton")!.IsEffectivelyVisible);
        window.Close();
    }

    // Two local branches, every write succeeding, and two commits in history (the newer one on top of the older, so
    // the graph has one lane); detached puts HEAD on the newer commit with the given number of commits of its own.
    private static AppHarness Harness(bool detached = false, int detachedCommits = 0)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: detached ? null : "main", head: HeadId, detachedCommits: detachedCommits);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        h.Branches.Local.Add("topic");
        h.History.Pages = (_, page) => new([Snapshots.Commit(HeadId, "Update image", Snapshots.Id('b')), Snapshots.Commit(Snapshots.Id('b'), "Initial")], false, page);
        return h;
    }

    // The window shown before the repository opens, then laid out over it.
    private static async Task<MainWindow> ShownAndOpened(ShellViewModel model)
    {
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        return window;
    }

    // The create dialog's steps, so the history and the deletion are checked after them as they always were.
    private static async Task TypeANameAndCancelTheCreateDialog(ShellViewModel model, MainWindow window)
    {
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        window.Descendant<TextBox>("NewBranchNameEditor")!.Text = "feature/ui"; AppHarness.Layout(window);
        window.Descendant<Button>("CancelCreateBranchButton")!.Command!.Execute(null); AppHarness.Layout(window);
    }

    // The history's steps: shown, its scope toggled there and back, and its newest commit selected.
    private static async Task BrowseTheHistory(ShellViewModel model, MainWindow window)
    {
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var scope = window.GetVisualDescendants().OfType<HistoryView>().Single().Descendant<Button>("HistoryScopeButton")!;
        scope.Command!.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        scope.Command!.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
    }
}
