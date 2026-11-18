using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;
using Trag.Desktop.Views;
using Trag.Desktop.Views.History;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views;

public sealed class DesktopLayoutTests
{
    private static ShellViewModel Model() => new AppHarness().Shell();
    [AvaloniaFact]
    public async Task WhenTheHistoryDividerIsDragged_ShouldResizeTheWholeCommitPaneAndRecoverBothEdges()
    {
        await using var model = Model(); model.Repository.Apply(Snapshots.Create()); model.Navigator.Section = WorkspaceSection.History;
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
        var panes = history.Descendant<RatioGrid>("HistoryPanels")!;
        var handle = history.Descendant<PaneSplitter>("HistoryDivider")!;
        var top = panes.Children[0]; var bottom = panes.Children[2];
        var before = bottom.Bounds.Height;
        AppHarness.Drag(window, handle, 0, -60);
        Assert.InRange(bottom.Bounds.Height - before, 58, 62);
        Assert.InRange(top.Bounds.Bottom + handle.Bounds.Height, bottom.Bounds.Y - 1, bottom.Bounds.Y + 1);
        AppHarness.Drag(window, handle, 0, 3000);
        Assert.Equal(1, model.Layout.HistoryPanelRatio); Assert.InRange(bottom.Bounds.Height, 0, 1);
        AppHarness.Drag(window, handle, 0, -3000);
        Assert.Equal(0, model.Layout.HistoryPanelRatio); Assert.InRange(top.Bounds.Height, 0, 1);
        handle.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);
        Assert.Equal(0.5, model.Layout.HistoryPanelRatio); Assert.True(bottom.Bounds.Height > 100);
        window.Close();
    }
    // The chosen commit's message gets a third of the commit pane the first time; from then on it keeps its height when the
    // history divider moves, and the files and diff take the difference.
    [AvaloniaFact]
    public async Task WhenTheHistoryDividerMoves_ShouldKeepTheMessageHeightAndResizeOnlyTheFilesAndDiff()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var details = await CommitChosenAsync(model, window);
        var panes = details.Descendant<RatioGrid>("CommitDetailsPanels")!;
        var message = panes.Children[0]; var files = panes.Children[2];
        var third = (panes.Bounds.Height - RatioGrid.SplitterThickness) / 3;
        Assert.InRange(message.Bounds.Height, third - 1, third + 1);
        var height = message.Bounds.Height; var filesBefore = files.Bounds.Height;
        var outer = window.GetVisualDescendants().OfType<HistoryView>().Single().Descendant<PaneSplitter>("HistoryDivider")!;

        AppHarness.Drag(window, outer, 0, -100);
        Assert.InRange(message.Bounds.Height, height - 1, height + 1);
        Assert.InRange(files.Bounds.Height - filesBefore, 98, 102);
        AppHarness.Drag(window, outer, 0, 150);
        Assert.InRange(message.Bounds.Height, height - 1, height + 1);
        Assert.InRange(model.Layout.CommitMessageHeight, height - 1, height + 1);
        window.Close();
    }

    // Past the files, the history divider takes the message, down to nothing; the divider between them stays on the pane
    // all along. Dragged back up, the message comes back first, to the height it keeps.
    [AvaloniaFact]
    public async Task WhenTheHistoryDividerIsDraggedPastTheFiles_ShouldShrinkTheMessageAndKeepItsDividerOnThePane()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var details = await CommitChosenAsync(model, window);
        var panes = details.Descendant<RatioGrid>("CommitDetailsPanels")!;
        var handle = details.Descendant<PaneSplitter>("CommitMessageDivider")!;
        var message = panes.Children[0]; var files = panes.Children[2];
        var height = message.Bounds.Height;
        var outer = window.GetVisualDescendants().OfType<HistoryView>().Single().Descendant<PaneSplitter>("HistoryDivider")!;

        AppHarness.Drag(window, outer, 0, files.Bounds.Height + 50);
        Assert.InRange(files.Bounds.Height, 0, 1);
        Assert.InRange(message.Bounds.Height, height - 52, height - 48);
        Assert.InRange(handle.Bounds.Bottom, panes.Bounds.Height - 1, panes.Bounds.Height + 1);
        AppHarness.Drag(window, outer, 0, -30);
        Assert.InRange(message.Bounds.Height, height - 22, height - 18);
        Assert.InRange(files.Bounds.Height, 0, 1);
        Assert.InRange(model.Layout.CommitMessageHeight, height - 1, height + 1);
        window.Close();
    }

    // The divider between the message and the files sets the message's height, up to the top of the pane; what does not
    // fit is cut at the line instead of drawn over the files, a collapsed message stays collapsed when the pane is shown
    // again, and Enter gives the message a third of the pane again.
    [AvaloniaFact]
    public async Task WhenTheMessageDividerIsDragged_ShouldSetTheMessageHeightUpToCollapsingItWithoutDrawingOverTheFiles()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var details = await CommitChosenAsync(model, window);
        var panes = details.Descendant<RatioGrid>("CommitDetailsPanels")!;
        var handle = details.Descendant<PaneSplitter>("CommitMessageDivider")!;
        var message = panes.Children[0];
        var before = message.Bounds.Height;

        AppHarness.Drag(window, handle, 0, 40);
        Assert.InRange(message.Bounds.Height - before, 38, 42);
        Assert.InRange(model.Layout.CommitMessageHeight - before, 38, 42);
        AppHarness.Drag(window, handle, 0, -2000);
        Assert.InRange(message.Bounds.Height, 0, 1);
        Assert.Equal(0, model.Layout.CommitMessageHeight);
        Assert.True(message.ClipToBounds);
        window.Height = 900; AppHarness.Layout(window);
        Assert.InRange(message.Bounds.Height, 0, 1);
        handle.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);
        var third = (panes.Bounds.Height - RatioGrid.SplitterThickness) / 3;
        Assert.InRange(message.Bounds.Height, third - 1, third + 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAMessageHeightWasSaved_ShouldShowTheMessageAtThatHeight()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Layout.CommitMessageHeight = 150;

        var details = await CommitChosenAsync(model, window);

        Assert.InRange(details.Descendant<RatioGrid>("CommitDetailsPanels")!.Children[0].Bounds.Height, 149, 151);
        window.Close();
    }

    // A message height saved while the pane was taller than it is now leaves the divider on the pane's edge, where it can
    // be dragged back, instead of pushing it and the files below the window.
    [AvaloniaFact]
    public async Task WhenTheSavedMessageIsTallerThanThePane_ShouldKeepTheDividerOnThePaneToDragBack()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Layout.CommitMessageHeight = 5000;

        var details = await CommitChosenAsync(model, window);
        var panes = details.Descendant<RatioGrid>("CommitDetailsPanels")!;
        var handle = details.Descendant<PaneSplitter>("CommitMessageDivider")!;

        Assert.InRange(handle.Bounds.Bottom, panes.Bounds.Height - 1, panes.Bounds.Height + 1);
        AppHarness.Drag(window, handle, 0, -100);
        Assert.InRange(panes.Children[2].Bounds.Height, 98, 102);
        window.Close();
    }

    // Only the commit's names share their row with the … button; the message under them scrolls across the whole pane,
    // its bar at the pane's right edge like the bars of the lists, not left of the button.
    [AvaloniaFact]
    public async Task WhenTheMessageScrolls_ShouldUseTheWholeWidthOfThePaneWithItsBarAtTheRightEdge()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var details = await CommitChosenAsync(model, window);
        var message = details.Descendant<RatioGrid>("CommitDetailsPanels")!.Children[0];
        var scroll = details.Descendant<ScrollViewer>("CommitMessageScroll")!;

        var right = scroll.TranslatePoint(new Point(scroll.Bounds.Width, 0), message)!.Value.X;

        Assert.InRange(right, message.Bounds.Width - 1, message.Bounds.Width + 1);
        window.Close();
    }

    // The divider moves from the height the message has: after History was shown anew with a saved height, and after the
    // history divider changed the pane, a drag adds to that height instead of starting from a third.
    [AvaloniaFact]
    public async Task WhenTheMessageDividerIsDraggedAfterHistoryWasShownAgainOrResized_ShouldMoveFromTheMessagesHeight()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Layout.CommitMessageHeight = 220;
        await CommitChosenAsync(model, window);
        model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        var details = window.GetVisualDescendants().OfType<CommitDetailsPanel>().Single();
        var message = details.Descendant<RatioGrid>("CommitDetailsPanels")!.Children[0];
        var handle = details.Descendant<PaneSplitter>("CommitMessageDivider")!;
        Assert.InRange(message.Bounds.Height, 219, 221);

        AppHarness.Drag(window, handle, 0, 20);
        Assert.InRange(message.Bounds.Height, 238, 242);
        AppHarness.Drag(window, window.GetVisualDescendants().OfType<HistoryView>().Single().Descendant<PaneSplitter>("HistoryDivider")!, 0, -100);
        AppHarness.Drag(window, handle, 0, 20);
        Assert.InRange(message.Bounds.Height, 258, 262);
        window.Close();
    }

    // A tall window, a repository, History, and its newest commit chosen; the divider waits for a commit to divide.
    private static async Task<CommitDetailsPanel> CommitChosenAsync(ShellViewModel model, MainWindow window)
    {
        window.Height = 1000;
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var details = window.GetVisualDescendants().OfType<CommitDetailsPanel>().Single();
        Assert.False(details.Descendant<PaneSplitter>("CommitMessageDivider")!.IsEffectivelyVisible);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        return details;
    }

    // A layout saved at its edges, the file lists folded away beside the diff and the unstaged list under the staged one,
    // the commit area at half the column and the commit's message at no height, leaves every divider under the pointer
    // and a double-click on it gives its panes back; the one between the lists once their column is back.
    [AvaloniaFact]
    public async Task WhenBothStatusPanesAreCollapsed_ShouldKeepEveryDividerReachableAndRestorable()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        model.Layout.DiffFilesRatio = 0; model.Layout.StagePanelRatio = 1; model.Layout.CommitPanelRatio = 0.5; model.Layout.CommitMessageHeight = 0;
        var window = h.Window(model); window.Height = 1000;
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);
        var lists = window.Descendant<RatioGrid>("StagePanels")!;
        Assert.Equal(0, lists.Bounds.Width);

        Restore(window, window.Descendant<PaneSplitter>("StatusFilesDivider")!);
        Assert.Equal(0.5, model.Layout.DiffFilesRatio);
        Restore(window, lists.Children.OfType<PaneSplitter>().Single());
        Assert.Equal(0.5, model.Layout.StagePanelRatio); Assert.True(window.Descendant<Control>("Unstaged")!.Bounds.Height > 100);
        Restore(window, window.Descendant<PaneSplitter>("CommitDivider")!);
        Assert.Equal(1, model.Layout.CommitPanelRatio);

        // The commit's files share the ratio of the file lists.
        model.Layout.DiffFilesRatio = 0;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        var details = window.GetVisualDescendants().OfType<CommitDetailsPanel>().Single();
        Restore(window, window.GetVisualDescendants().OfType<HistoryView>().Single().Descendant<PaneSplitter>("HistoryDivider")!);
        Assert.Equal(0.5, model.Layout.HistoryPanelRatio);
        Restore(window, details.Descendant<PaneSplitter>("CommitMessageDivider")!);
        Assert.True(model.Layout.CommitMessageHeight > 100);
        Restore(window, details.Descendant<PaneSplitter>("HistoryFilesDivider")!);
        Assert.Equal(0.5, model.Layout.DiffFilesRatio);
        window.Close();
    }

    // The divider is what the pointer at its middle hits, and a double-click there puts its panes back.
    private static void Restore(Window window, PaneSplitter divider)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); AppHarness.Layout(window);
        Assert.Same(divider, window.InputHitTest(divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window)!.Value));
        AppHarness.DoubleTap(window, divider);
    }

    [AvaloniaFact]
    public async Task WhenTheFileDividerIsDragged_ShouldReachBothEdgesAndKeepItsRatioWhenTheWindowResizes()
    {
        await using var model = Model(); model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        var pane = window.Descendant<RatioGrid>("StatusFilePanels")!;
        var handle = window.Descendant<PaneSplitter>("StatusFilesDivider")!;
        var before = pane.Children[0].Bounds.Width;
        AppHarness.Drag(window, handle, 70, 0);
        Assert.InRange(pane.Children[0].Bounds.Width - before, 68, 72);
        var ratio = model.Layout.DiffFilesRatio; window.Width = 1450; AppHarness.Layout(window);
        Assert.Equal(ratio, model.Layout.DiffFilesRatio);
        AppHarness.Drag(window, handle, -4000, 0); Assert.Equal(0, model.Layout.DiffFilesRatio); Assert.InRange(pane.Children[0].Bounds.Width, 0, 1);
        AppHarness.Drag(window, handle, 4000, 0); Assert.Equal(1, model.Layout.DiffFilesRatio); Assert.InRange(pane.Children[2].Bounds.Width, 0, 1);
        handle.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);
        Assert.Equal(0.5, model.Layout.DiffFilesRatio); window.Close();
    }
    [AvaloniaFact]
    public async Task WhenTheCommitDividerIsDragged_ShouldStayBetweenHalfTheColumnAndTheCommitAreasNaturalHeight()
    {
        await using var model = Model(); model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        var panels = window.Descendant<RatioGrid>("CommitPanels")!; var handle = window.Descendant<PaneSplitter>("CommitDivider")!;
        var area = window.Descendant<Border>("CommitArea")!; var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        var natural = area.Bounds.Height;
        Assert.InRange(natural, 100, 240); Assert.Equal(1, model.Layout.CommitPanelRatio); Assert.True(panels.EffectiveRatio < 1);
        var half = (panels.Bounds.Height - RatioGrid.SplitterThickness) / 2;
        AppHarness.Drag(window, handle, 0, -3000);
        Assert.Equal(0.5, model.Layout.CommitPanelRatio); Assert.InRange(area.Bounds.Height, half - 1, half + 1); Assert.True(editor.Bounds.Height > 100);
        AppHarness.Drag(window, handle, 0, 3000);
        Assert.Equal(1, model.Layout.CommitPanelRatio); Assert.InRange(area.Bounds.Height, natural - 1, natural + 1); Assert.InRange(editor.Bounds.Height, 63, 65);
        AppHarness.Drag(window, handle, 0, -120);
        Assert.True(area.Bounds.Height > natural + 100); Assert.InRange(model.Layout.CommitPanelRatio, 0.5, 0.999);
        handle.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);
        Assert.Equal(1, model.Layout.CommitPanelRatio); Assert.InRange(area.Bounds.Height, natural - 1, natural + 1);
        window.Close();
    }
    // Without the commit area (a merge or rebase in progress, or no repository) the upper pane takes the whole column.
    [AvaloniaFact]
    public async Task WhenTheRepositoryCloses_ShouldHideTheCommitDividerTrack()
    {
        await using var model = Model(); model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        DragTheCommitDividerToItsLimitsAndBack(window, window.Descendant<PaneSplitter>("CommitDivider")!);
        await model.CloseRepositoryCommand.ExecuteAsync(null); model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(window.Descendant<PaneSplitter>("CommitDivider")!.IsEffectivelyVisible);
        window.Close();
    }
    // A merge or a rebase in progress is finished in its own view.
    [AvaloniaTheory]
    [InlineData(RepositoryOperation.Merge)]
    [InlineData(RepositoryOperation.Rebase)]
    public async Task WhenAMergeOrARebaseIsInProgress_ShouldHideTheCommitAreaInTheFileStatus(RepositoryOperation operation)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = operation == RepositoryOperation.Merge
            ? Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""))
            : Snapshots.Rebasing(files: [Snapshots.Conflict("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(window.Workspace.Composer.IsEffectivelyVisible);
        Assert.True(window.Descendant<RatioGrid>("CommitPanels")!.IsSecondPaneCollapsed);
        Assert.Equal(0, window.Descendant<PaneSplitter>("CommitDivider")!.Bounds.Height);
        window.Close();
    }
    // The actions over a conflict, a side for the whole file, the external tools and Mark as resolved, take more lines when
    // the detail pane is narrower than one of them, instead of running past its edge, where nothing shows they are there.
    [AvaloniaFact]
    public async Task WhenTheConflictDetailIsNarrowerThanItsActions_ShouldKeepEveryActionInside()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Resolution.SelectedConflict = model.Resolution.Conflicts[0]; AppHarness.Layout(window);
        var detail = window.GetVisualDescendants().OfType<Trag.Desktop.Views.Merge.ConflictDetailPanel>().Single();

        string[] actions = ["UseYoursButton", "UseOtherButton", "ConflictEditorButton", "MergeToolButton", "StageResolutionButton"];
        var outside = actions.Where(name => detail.Descendant<Button>(name)!.TranslatePoint(new(detail.Descendant<Button>(name)!.Bounds.Width, 0), detail) is not { X: var right } || right > detail.Bounds.Width);

        Assert.Empty(outside);
        window.Close();
    }

    // Why Mark as resolved waits takes more lines in a pane too narrow for it on one, instead of being cut at its edge.
    [AvaloniaFact]
    public async Task WhenTheConflictDetailIsNarrow_ShouldWrapWhyMarkAsResolvedWaits()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""))
            with { Resolutions = new([], [new(Snapshots.Path("a.txt"), 1)]) };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Resolution.SelectedConflict = model.Resolution.Conflicts[0]; AppHarness.Layout(window);
        var detail = window.GetVisualDescendants().OfType<Trag.Desktop.Views.Merge.ConflictDetailPanel>().Single();

        var reason = detail.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == MergeText.MarkResolvedWaits);

        Assert.True(reason.IsEffectivelyVisible);
        Assert.True(reason.TranslatePoint(new(reason.Bounds.Width, 0), detail)!.Value.X <= detail.Bounds.Width);
        window.Close();
    }

    // How to settle a file one side deleted takes more lines in a narrow pane: the code of a conflict may scroll sideways,
    // the sentence over it stays within what shows.
    [AvaloniaFact]
    public async Task WhenAFileDeletedOnOneSideIsShownInANarrowPane_ShouldWrapHowToSettleIt()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("a.txt"), 'D', 'U', IsConflict: true)], operation: RepositoryOperation.Merge,
            merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Resolution.SelectedConflict = model.Resolution.Conflicts[0]; AppHarness.Layout(window);
        var blocks = window.Descendant<ScrollViewer>("ConflictBlocks")!;

        var sentence = blocks.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text?.StartsWith("Use ", StringComparison.Ordinal) == true);

        Assert.True(sentence.TranslatePoint(new(sentence.Bounds.Width, 0), blocks)!.Value.X <= blocks.Viewport.Width);
        window.Close();
    }

    // The actions over a block take another line in a narrow pane, instead of lying past what shows, where only a sideways
    // scroll reaches them; only a line of code too long for the pane scrolls sideways.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAConflictBlockIsShownInANarrowPane_ShouldKeepTakeBothInside(bool longLine)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
        var theirs = longLine ? new string('x', 400) : "theirs";
        h.Diff.Conflict = _ => new(string.Join('\n', "a", "<<<<<<< HEAD", "mine", "=======", theirs, ">>>>>>> incoming", "z", ""), "hash");
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        model.Resolution.SelectedConflict = model.Resolution.Conflicts[0]; AppHarness.Layout(window);
        var blocks = window.Descendant<ScrollViewer>("ConflictBlocks")!;

        var takeBoth = blocks.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Take both"));

        Assert.True(takeBoth.TranslatePoint(new(takeBoth.Bounds.Width, 0), blocks)!.Value.X <= blocks.Viewport.Width);
        Assert.Equal(longLine, blocks.Extent.Width > blocks.Viewport.Width);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenNothingIsOpen_ShouldKeepTheEmptyDiffsHiddenInTheFileStatusAndSearchAndOfferNoWrites()
    {
        await using var model = Model();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        Assert.False(window.Workspace.Diff.FileDiffView.IsVisible); // no selection: the diff panel keeps it hidden before it is even laid out
        Assert.Null(window.Descendant<Button>("ToolbarCommitButton")); Assert.False(window.Descendant<Button>("StashButton")!.IsEffectivelyEnabled);
        model.Navigator.ShowSearchCommand.Execute(null); AppHarness.Layout(window);
        var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
        Assert.False(history.Descendant<DiffView>("CommitDiffView")!.IsEffectivelyVisible);
        window.Close();
    }
    [AvaloniaFact]
    public async Task WhenTheWindowShows_ShouldNameTheAppInTheTitleBarAndCenterTheRepositoryName()
    {
        await using var model = Model();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        Assert.Equal("Trag - Git Client", window.Title);
        var bar = window.Descendant<Border>("TitleBar")!;
        Assert.Equal(0, bar.Bounds.Y); Assert.Equal(30, bar.Bounds.Height);
        Assert.True(window.Descendant<Button>("OpenButton")!.TranslatePoint(default, window)!.Value.Y >= 30);
        var name = window.Descendant<TextBlock>("TitleRepositoryName")!;
        Assert.Equal("No repository open", name.Text);
        model.Repository.Apply(Snapshots.Create(root: "website")); AppHarness.Layout(window);
        Assert.Equal("website", name.Text);
        var center = name.TranslatePoint(new Point(name.Bounds.Width / 2, 0), window)!.Value.X;
        Assert.InRange(center, window.Bounds.Width / 2 - 1, window.Bounds.Width / 2 + 1);
        Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "website");
        Assert.True(window.Descendant<TextBlock>("TitleAppName")!.IsEffectivelyVisible);
        window.Close();
    }

    // The drags and the Enter of the commit divider test, so a later check finds the divider where that test leaves it.
    private static void DragTheCommitDividerToItsLimitsAndBack(MainWindow window, PaneSplitter handle)
    {
        AppHarness.Drag(window, handle, 0, -3000);
        AppHarness.Drag(window, handle, 0, 3000);
        AppHarness.Drag(window, handle, 0, -120);
        handle.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);
    }
}
