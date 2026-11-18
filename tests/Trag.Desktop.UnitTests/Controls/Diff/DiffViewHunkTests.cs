using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Diff;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;
using Trag.Desktop.Dialogs;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.Desktop.UnitTests.Controls.Diff;

// The hunk actions on the diff: small buttons beside the first row in view of what they take, which is the block under
// the pointer unless lines of it are chosen, and otherwise the chosen lines; a click chooses a line, Shift a range, Ctrl
// one more. Chosen lines are named as lines unless they are a whole block. The buttons are not there when the view
// offers no action or nothing is pointed at or chosen. Discard sits left of the stage action, only where it is offered.
// A right-click offers both the chosen lines and the hunk under the pointer, staged or discarded. The buttons are
// transparent icons, a plus to stage and a minus to unstage, named in their tooltips.
public sealed class DiffViewHunkTests
{
    // d to D and f to F: blocks 3–4 and 6–7 between unchanged lines.
    private static readonly PatchLine[] TwoChanges =
    [
        new(' ', " a", 1, 1), new(' ', " b", 2, 2), new(' ', " c", 3, 3), new('-', "-d", 4), new('+', "+D", null, 4), new(' ', " e", 5, 5),
        new('-', "-f", 6), new('+', "+F", null, 6), new(' ', " g", 7, 7)
    ];

    private static async Task<(Window Window, DiffView View, List<object?> Taken)> ShowAsync(IReadOnlyList<PatchLine> lines, string action = "Stage hunk")
    {
        var document = await new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "sample.txt", default);
        List<object?> taken = [];
        var view = new DiffView { Document = document, Offer = new HunkOffer(action, "Stage lines", "", "", false), HunkCommand = new RelayCommand<object?>(taken.Add) };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        AppHarness.Layout(window);
        return (window, view, taken);
    }

    private static ListBox Rows(DiffView view) => view.Descendant<ListBox>("Rows")!;
    private static Button Action(DiffView view) => view.Descendant<Button>("HunkButton")!;
    private static Button Discard(DiffView view) => view.Descendant<Button>("DiscardHunkButton")!;

    private static MenuFlyout Menu(DiffView view) => Assert.IsType<MenuFlyout>(Rows(view).ContextFlyout);
    private static string[] Headers(MenuFlyout menu) => menu.Items.OfType<MenuItem>().Select(item => (string)item.Header!).ToArray();
    private static void Run(MenuFlyout menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().Single(candidate => (string?)candidate.Header == header);
        item.Command!.Execute(item.CommandParameter);
    }

    private static int[][] Lines(List<object?> taken) => taken.Select(choice => ((HunkChoice)choice!).Lines.ToArray()).ToArray();

    private static void RightClickRow(Window window, DiffView view, int row)
    {
        var point = Rows(view).TranslatePoint(new Point(200, row * DiffViewport.RowHeight + DiffViewport.RowHeight / 2 - Scroller(view).Offset.Y), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        AppHarness.Layout(window);
    }

    // A click on a row, with Shift or Ctrl held when asked.
    private static void ClickRow(Window window, DiffView view, int row, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = Rows(view).TranslatePoint(new Point(200, row * DiffViewport.RowHeight + DiffViewport.RowHeight / 2 - Scroller(view).Offset.Y), window)!.Value;
        window.MouseMove(point, modifiers);
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        AppHarness.Layout(window);
    }
    private static string Icon(Button button) => Assert.IsType<LineIcon>(button.Content).Kind;
    private static ScrollViewer Scroller(DiffView view) => Rows(view).GetVisualDescendants().OfType<ScrollViewer>().First();

    private static void PointAt(Window window, DiffView view, int row)
    {
        var y = row * DiffViewport.RowHeight + DiffViewport.RowHeight / 2 - Scroller(view).Offset.Y;
        window.MouseMove(Rows(view).TranslatePoint(new Point(200, y), window)!.Value);
        AppHarness.Layout(window);
    }

    // The row the action sits in, whole.
    private static int RowOf(DiffView view)
    {
        var top = Action(view).TranslatePoint(default, Rows(view))!.Value.Y + Scroller(view).Offset.Y;
        var row = (int)(top / DiffViewport.RowHeight);
        Assert.True(top + Action(view).Bounds.Height <= (row + 1) * DiffViewport.RowHeight, "the action fits in one row");
        return row;
    }

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        AppHarness.Layout(window);
    }

    [AvaloniaFact]
    public async Task WhenThePointerIsOverAChange_ShouldOfferItsHunkBesideItsFirstRowAndTakeIt()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);

        PointAt(window, view, 7);

        Assert.True(Action(view).IsVisible);
        Assert.Equal("Stage hunk", ToolTip.GetTip(Action(view)));
        Assert.Equal(6, RowOf(view));
        Click(window, Action(view));
        Assert.Equal(new DiffBlock(6, 7).Lines, ((HunkChoice)Assert.Single(taken)!).Lines);
        Assert.Same(view.Document.Lines, ((HunkChoice)taken[0]!).Document);
        window.Close();
    }

    // A line of a change chosen with the keyboard, the pointer elsewhere: that line, not its whole block.
    [AvaloniaFact]
    public async Task WhenALineOfAChangeIsSelectedWithThePointerElsewhere_ShouldOfferThatLine()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);

        Rows(view).SelectedIndex = 4;
        AppHarness.Layout(window);

        Assert.True(Action(view).IsVisible);
        Assert.Equal(("Stage lines", 4), (ToolTip.GetTip(Action(view)), RowOf(view)));
        Click(window, Action(view));
        Assert.Equal([4], ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    // A block holds a.. "-b", "-c", "+B", "+C": a click and a Shift+click choose two of its lines, which stay lines.
    [AvaloniaFact]
    public async Task WhenLinesAreChosenWithShift_ShouldOfferThemBesideTheFirstAsLines()
    {
        PatchLine[] lines = [new(' ', " a", 1, 1), new('-', "-b", 2), new('-', "-c", 3), new('+', "+B", null, 2), new('+', "+C", null, 3), new(' ', " d", 4, 4)];
        var (window, view, taken) = await ShowAsync(lines);
        view.Offer = view.Offer with { DiscardHunk = "Discard hunk" };
        view.Offer = view.Offer with { DiscardLines = "Discard lines" };

        ClickRow(window, view, 2);
        ClickRow(window, view, 3, RawInputModifiers.Shift);
        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        Assert.Equal(("Stage lines", 2), (ToolTip.GetTip(Action(view)), RowOf(view)));
        Assert.Equal("Discard lines", ToolTip.GetTip(Discard(view)));
        Assert.Equal(("Stage lines", "Discard lines"), (AutomationProperties.GetName(Action(view)), AutomationProperties.GetName(Discard(view))));
        Click(window, Action(view));
        Assert.Equal([2, 3], ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheViewNamesTheLineActionsLate_ShouldOfferThemThen()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);
        view.Offer = view.Offer with { Lines = "" };
        ClickRow(window, view, 4);
        Assert.False(Action(view).IsVisible);

        view.Offer = view.Offer with { Lines = "Unstage lines" };
        view.Offer = view.Offer with { DiscardLines = "Discard lines" };
        AppHarness.Layout(window);

        Assert.Equal("Unstage lines", ToolTip.GetTip(Action(view)));
        Assert.True(Discard(view).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenLinesOfTwoChangesAreChosenWithCtrl_ShouldOfferThemTogether()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);

        ClickRow(window, view, 4);
        ClickRow(window, view, 7, RawInputModifiers.Control);
        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        Assert.Equal(("Stage lines", 4), (ToolTip.GetTip(Action(view)), RowOf(view)));
        Click(window, Action(view));
        Assert.Equal([4, 7], ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenShiftAndAnArrowExtendTheChoice_ShouldOfferTheChosenLines()
    {
        PatchLine[] lines = [new(' ', " a", 1, 1), new('-', "-b", 2), new('-', "-c", 3), new('+', "+B", null, 2), new('+', "+C", null, 3), new(' ', " d", 4, 4)];
        var (window, view, taken) = await ShowAsync(lines);
        ClickRow(window, view, 1);

        window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        Click(window, Action(view));
        Assert.Equal([1, 2], ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    // In split mode a row shows a removed line beside its replacement, and choosing the row chooses both.
    [AvaloniaFact]
    public async Task WhenSplitRowsAreChosen_ShouldChooseBothSidesOfEach()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);
        view.Mode = 1;
        AppHarness.Layout(window);

        ClickRow(window, view, 3);
        ClickRow(window, view, 5, RawInputModifiers.Control);
        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        Assert.Equal(("Stage lines", 3), (ToolTip.GetTip(Action(view)), RowOf(view)));
        Click(window, Action(view));
        Assert.Equal([3, 4, 6, 7], ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheActionStagesOrUnstages_ShouldShowAPlusOrAMinus()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);

        PointAt(window, view, 3);
        Assert.Equal("plus", Icon(Action(view)));

        view.Offer = view.Offer with { Unstages = true };
        AppHarness.Layout(window);
        Assert.Equal("minus", Icon(Action(view)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheActionsAreShown_ShouldBeTransparentIconsWithoutABorder()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);
        view.Offer = view.Offer with { DiscardHunk = "Discard hunk" };

        PointAt(window, view, 3);

        foreach (var button in new[] { Action(view), Discard(view) })
        {
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
            Assert.Equal(default, button.BorderThickness);
        }
        Assert.Equal("discard", Icon(Discard(view)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThePointerLeavesTheRows_ShouldOfferTheChosenLine()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);
        Rows(view).SelectedIndex = 7;
        PointAt(window, view, 3);
        Assert.Equal(3, RowOf(view));

        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        Assert.True(Action(view).IsVisible);
        Assert.Equal(7, RowOf(view));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThePointerIsOverUnchangedLinesAndNoneIsSelected_ShouldOfferNoHunk()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);

        PointAt(window, view, 0);

        Assert.False(Action(view).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheViewOffersNoHunkAction_ShouldShowNoButtonOverAChangeUntilItDoes()
    {
        var (window, view, _) = await ShowAsync(TwoChanges, action: "");

        PointAt(window, view, 3);
        Assert.False(Action(view).IsVisible);

        view.Offer = view.Offer with { Hunk = "Unstage hunk" };
        AppHarness.Layout(window);
        Assert.True(Action(view).IsVisible);
        Assert.Equal("Unstage hunk", ToolTip.GetTip(Action(view)));
        window.Close();
    }

    // The hunk was taken and the file read again: what was the block is unchanged now, so nothing is offered for it.
    [AvaloniaFact]
    public async Task WhenTheDiffIsReadAgainWithoutThePointedHunk_ShouldTakeItsActionAway()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);
        PointAt(window, view, 7);
        Assert.True(Action(view).IsVisible);

        PatchLine[] rest = [.. TwoChanges[..6], new(' ', " F", 6, 6), new(' ', " g", 7, 7)];
        var again = await new BackgroundDiffBuilder().BuildAsync(new([], rest, DiffContentKind.Text, ""), "sample.txt", default);
        view.Document = again with { Source = view.Document.Source };
        AppHarness.Layout(window);

        Assert.False(Action(view).IsVisible);
        window.Close();
    }

    // The selected change starts below the view; scrolled to, the action comes with it.
    [AvaloniaFact]
    public async Task WhenTheSelectedChangeIsScrolledIntoView_ShouldBringItsActionAlong()
    {
        PatchLine[] lines =
        [
            .. Enumerable.Range(1, 60).Select(number => new PatchLine(' ', " line", number, number)), new('-', "-x", 61), new('+', "+X", null, 61),
            .. Enumerable.Range(62, 40).Select(number => new PatchLine(' ', " line", number, number))
        ];
        var (window, view, _) = await ShowAsync(lines);
        Rows(view).SelectedIndex = 60;
        AppHarness.Layout(window);
        Scroller(view).Offset = default;
        AppHarness.Layout(window);
        Assert.False(Action(view).IsVisible);

        Scroller(view).Offset = new(0, 50 * DiffViewport.RowHeight);
        AppHarness.Layout(window);

        Assert.True(Action(view).IsVisible);
        Assert.Equal(60, RowOf(view));
        window.Close();
    }

    // Scrolled down, the pointer points at the change on screen, not at the one as far from the top of the file.
    [AvaloniaFact]
    public async Task WhenThePointerIsOverAChangeInAScrolledView_ShouldOfferThatChangesHunk()
    {
        PatchLine[] lines =
        [
            .. Enumerable.Range(1, 10).Select(number => new PatchLine(' ', " line", number, number)), new('-', "-w", 11), new('+', "+W", null, 11),
            .. Enumerable.Range(12, 48).Select(number => new PatchLine(' ', " line", number, number)), new('-', "-x", 60), new('+', "+X", null, 60),
            .. Enumerable.Range(61, 40).Select(number => new PatchLine(' ', " line", number, number))
        ];
        var (window, view, taken) = await ShowAsync(lines);
        Scroller(view).Offset = new(0, 50 * DiffViewport.RowHeight);
        AppHarness.Layout(window);

        PointAt(window, view, 61);

        Assert.True(Action(view).IsVisible);
        Assert.Equal(60, RowOf(view));
        Click(window, Action(view));
        Assert.Equal(new DiffBlock(60, 61).Lines, ((HunkChoice)Assert.Single(taken)!).Lines);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAWorkingTreeHunkIsPointedAt_ShouldOfferItsDiscardLeftOfTheStageActionAndTakeIt()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);
        List<object?> discarded = [];
        view.Offer = view.Offer with { DiscardHunk = "Discard hunk" };
        view.DiscardHunkCommand = new RelayCommand<object?>(discarded.Add);

        PointAt(window, view, 7);

        Assert.True(Discard(view).IsVisible);
        Assert.Equal("Discard hunk", ToolTip.GetTip(Discard(view)));
        var (discard, stage) = (Discard(view).TranslatePoint(default, Rows(view))!.Value, Action(view).TranslatePoint(default, Rows(view))!.Value);
        Assert.Equal(stage.Y, discard.Y);
        Assert.True(discard.X + Discard(view).Bounds.Width <= stage.X, "discard sits left of the stage action");
        Click(window, Discard(view));
        Assert.Equal(new DiffBlock(6, 7).Lines, ((HunkChoice)Assert.Single(discarded)!).Lines);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheViewOffersNoDiscard_ShouldShowTheStageActionAloneUntilItDoes()
    {
        var (window, view, _) = await ShowAsync(TwoChanges, action: "Unstage hunk");

        PointAt(window, view, 3);
        Assert.True(Action(view).IsVisible);
        Assert.False(Discard(view).IsVisible);

        view.Offer = view.Offer with { DiscardHunk = "Discard hunk" };
        AppHarness.Layout(window);
        Assert.True(Discard(view).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheViewTakesTheActionsAway_ShouldHideTheDiscardToo()
    {
        var (window, view, _) = await ShowAsync(TwoChanges, action: "");
        view.Offer = view.Offer with { DiscardHunk = "Discard hunk" };

        PointAt(window, view, 3);

        Assert.False(Discard(view).IsVisible);
        window.Close();
    }

    // The file status binds the discard to the shown working diff: a red question first, then that hunk goes.
    [AvaloniaFact]
    public async Task WhenAWorkingTreeHunkIsDiscarded_ShouldAskInARedDialogAndDiscardItOnConfirm()
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        harness.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;

            PointAt(window, view, 3);
            Click(window, Discard(view));

            var dialog = window.Descendant<DialogHost>("DiscardHunkOverlay")!;
            Assert.True(dialog.IsEffectivelyVisible);
            Assert.Contains("danger", window.Descendant<Button>("ConfirmDiscardHunkButton")!.Classes);
            Assert.True(window.Descendant<Button>("CancelDiscardHunkButton")!.IsFocused);
            var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? "").ToArray();
            Assert.Contains("Discard this hunk?", texts); Assert.Contains("a.txt", texts);
            Assert.Empty(harness.Writer.Requests);

            Click(window, window.Descendant<Button>("ConfirmDiscardHunkButton")!);
            await AppHarness.Until(() => harness.Writer.LastRequest is PatchRequest, "the hunk discarded");

            var request = Assert.IsType<PatchRequest>(harness.Writer.LastRequest);
            Assert.Equal((PatchAction.Discard, HunkPatch.For(TwoChanges, new DiffBlock(3, 4).Lines, Snapshots.Path("a.txt"), PatchAction.Discard)), (request.Action, request.Patch));
            await AppHarness.Until(() => !dialog.IsEffectivelyVisible, "the question closed");
        }
        finally { window.Close(); }
    }

    // A right-click on a line of a two-line change chooses the line, and the menu offers it and its whole hunk.
    [AvaloniaFact]
    public async Task WhenALineOfAChangeIsRightClicked_ShouldOfferTheLineAndItsHunkStagedOrDiscarded()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);
        List<object?> discarded = [];
        view.Offer = view.Offer with { DiscardHunk = "Discard hunk", DiscardLines = "Discard lines" };
        view.DiscardHunkCommand = new RelayCommand<object?>(discarded.Add);

        RightClickRow(window, view, 4);

        var menu = Menu(view);
        Assert.True(menu.IsOpen);
        Assert.Equal(["Stage lines", "Stage hunk", "Discard lines", "Discard hunk"], Headers(menu));
        Assert.Single(menu.Items.OfType<Separator>());
        Assert.Contains("actions", menu.FlyoutPresenterClasses);
        Assert.Equal([null, null, "discard", "discard"], menu.Items.OfType<MenuItem>().Select(item => (item.Icon as LineIcon)?.Kind));
        Run(menu, "Stage lines"); Run(menu, "Stage hunk"); Run(menu, "Discard lines"); Run(menu, "Discard hunk");
        Assert.Equal([[4], [3, 4]], Lines(taken));
        Assert.Equal([[4], [3, 4]], Lines(discarded));
        Assert.All(taken.Concat(discarded), choice => Assert.Same(view.Document.Lines, ((HunkChoice)choice!).Document));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAnUnchangedLineIsRightClicked_ShouldOpenNoMenu()
    {
        var (window, view, _) = await ShowAsync(TwoChanges);

        RightClickRow(window, view, 0);

        Assert.False(Menu(view).IsOpen);
        window.Close();
    }

    // The menu key with the pointer away: the hunk is the first chosen line's.
    [AvaloniaFact]
    public async Task WhenTheMenuIsOpenedFromTheKeyboard_ShouldOfferTheChosenLinesAndTheirHunk()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);
        ClickRow(window, view, 4);
        window.MouseMove(new Point(450, 2));
        AppHarness.Layout(window);

        window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        AppHarness.Layout(window);

        var menu = Menu(view);
        Assert.True(menu.IsOpen);
        Run(menu, "Stage lines"); Run(menu, "Stage hunk");
        Assert.Equal([[4], [3, 4]], Lines(taken));
        window.Close();
    }

    // Without the mouse: the rows take focus, the arrows choose a line, and the menu key or Shift+F10 offers it and its hunk.
    [AvaloniaTheory]
    [InlineData("Menu")]
    [InlineData("Shift+F10")]
    public async Task WhenTheKeyboardAloneChoosesLinesAndPressesTheMenuKey_ShouldStageThoseLines(string keys)
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);
        Rows(view).Focus();
        AppHarness.Layout(window);

        for (var row = 0; row < 4; row++) window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        AppHarness.AskForMenu(window, keys);
        AppHarness.Layout(window);

        var menu = Menu(view);
        Assert.True(menu.IsOpen);
        Run(menu, "Stage lines"); Run(menu, "Stage hunk");
        Assert.Equal([[3], [3, 4]], Lines(taken));
        window.Close();
    }

    // A right-click on a chosen line keeps the choice, and the hunk is the one under the pointer.
    [AvaloniaFact]
    public async Task WhenAChosenLineIsRightClicked_ShouldKeepTheChoiceAndOfferTheHunkUnderThePointer()
    {
        var (window, view, taken) = await ShowAsync(TwoChanges);
        ClickRow(window, view, 4);
        ClickRow(window, view, 7, RawInputModifiers.Control);

        RightClickRow(window, view, 7);

        var menu = Menu(view);
        Assert.Equal(["Stage lines", "Stage hunk"], Headers(menu));
        Assert.Empty(menu.Items.OfType<Separator>());
        Run(menu, "Stage lines"); Run(menu, "Stage hunk");
        Assert.Equal([[4, 7], [6, 7]], Lines(taken));
        window.Close();
    }

    // The file status binds the menu to the shown working diff: its discard asks first, like the button.
    [AvaloniaFact]
    public async Task WhenAHunkIsDiscardedFromTheMenuInTheFileStatus_ShouldAskAboutThatHunk()
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        harness.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;

            RightClickRow(window, view, 7);
            Run(Menu(view), "Discard hunk");

            Assert.True(model.WorkingDiff.HunkDiscard.IsOpen);
            Assert.Equal([6, 7], model.WorkingDiff.HunkDiscard.Subject!.Chosen);
            Assert.Empty(harness.Writer.Requests);
        }
        finally { window.Close(); }
    }

    // The file status binds the side: a staged file's hunk goes back out with a minus.
    [AvaloniaFact]
    public async Task WhenTheFileStatusShowsAStagedFile_ShouldOfferAMinusToUnstageItsHunk()
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")]);
        harness.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;

            PointAt(window, view, 3);

            Assert.Equal(("minus", "Unstage hunk"), (Icon(Action(view)), ToolTip.GetTip(Action(view))));
        }
        finally { window.Close(); }
    }

    // The file status names the line actions too: a chosen line of the shown diff is staged alone.
    [AvaloniaFact]
    public async Task WhenALineIsChosenInTheFileStatus_ShouldStageThatLineAlone()
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        harness.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;

            ClickRow(window, view, 4);
            Assert.Equal("Stage lines", ToolTip.GetTip(Action(view)));
            Assert.Equal("Discard lines", ToolTip.GetTip(Discard(view)));
            Click(window, Action(view));
            await AppHarness.Until(() => harness.Writer.LastRequest is PatchRequest, "the line staged");

            Assert.Equal(HunkPatch.For(TwoChanges, [4], Snapshots.Path("a.txt"), PatchAction.Stage), Assert.IsType<PatchRequest>(harness.Writer.LastRequest).Patch);
        }
        finally { window.Close(); }
    }

    // The file status binds the action to the shown working diff: the button stages that hunk of the file.
    [AvaloniaFact]
    public async Task WhenTheFileStatusShowsAModifiedFile_ShouldStageTheHunkItsButtonIsBeside()
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        harness.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;

            PointAt(window, view, 3);
            Assert.Equal("Stage hunk", ToolTip.GetTip(Action(view)));
            Click(window, Action(view));
            await AppHarness.Until(() => harness.Writer.LastRequest is PatchRequest, "the hunk staged");

            var request = Assert.IsType<PatchRequest>(harness.Writer.LastRequest);
            Assert.Equal(HunkPatch.For(model.WorkingDiff.Diff.Document.Lines, new DiffBlock(3, 4).Lines, Snapshots.Path("a.txt"), PatchAction.Stage), request.Patch);
        }
        finally { window.Close(); }
    }
}
