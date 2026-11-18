using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Dialogs;

// The dialog for editing the current branch's commits, in the real window: the commits oldest first, each with a choice
// of what happens to it, dimmed when dropped and indented when joined into the one above; Alt and an arrow or the two
// buttons move the chosen commit; its message shows below, written where a reword or a squash sets it; what keeps the
// edit back or what the upstream has is said above Edit commits.
public sealed class RewriteDialogTests
{
    private static readonly string Older = Snapshots.Id('b');

    private static AppHarness Harness()
    {
        var h = new AppHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        h.Branches.Rewrites[Older] = new(Snapshots.Id('c'),
            [new(Older, "Older commit", "Older commit\n\nBody", RewriteAction.Pick, "Older commit\n\nBody"),
             new(Snapshots.HeadId, "Update image", "Update image", RewriteAction.Pick, "Update image")], 0);
        return h;
    }

    // History shown, the older commit chosen and the dialog opened for it.
    private static async Task<MainWindow> OpenedAsync(AppHarness h, ShellViewModel model)
    {
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null);
        AppHarness.Layout(window);
        return window;
    }

    private static DialogHost Dialog(Window window) => window.Descendant<DialogHost>("RewriteOverlay")!;
    private static ListBox Rows(Window window) => window.Descendant<ListBox>("RewriteRows")!;
    private static ComboBox Choice(Window window, int row) => ((Control)Rows(window).ContainerFromIndex(row)!).GetVisualDescendants().OfType<ComboBox>().Single();
    private static string[] Texts(Window window) => [.. Dialog(window).GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? "")];

    [AvaloniaFact]
    public async Task WhenTheDialogOpens_ShouldListTheCommitsWithTheirActionsAndWaitForAChange()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);

        Assert.True(Dialog(window).IsEffectivelyVisible);
        Assert.Contains("Edit commits on main", Texts(window));
        Assert.Contains("2 commits, from " + Older[..7] + " to the tip", Texts(window));
        Assert.Equal(2, Rows(window).ItemCount);
        Assert.Equal(RewriteAction.Pick, Choice(window, 1).SelectedValue);
        Assert.Contains(RewriteText.Unchanged, Texts(window));
        Assert.False(window.Descendant<TextBlock>("RewriteWarning")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("StartRewriteButton")!.IsEffectivelyEnabled);
        var message = window.Descendant<TextBox>("RewriteMessage")!;
        Assert.Equal(("Older commit\n\nBody", true), (message.Text, message.IsReadOnly));
        Assert.True(Rows(window).IsKeyboardFocusWithin);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAnActionIsChosenInARow_ShouldSetItAndDimOrIndentTheRow()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var row = (Control)Rows(window).ContainerFromIndex(1)!;

        Choice(window, 1).SelectedValue = RewriteAction.Drop;
        AppHarness.Layout(window);
        Assert.Equal(RewriteAction.Drop, model.Rewrite.Rows[1].Action);
        Assert.Contains("dropped", row.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("rewriterow")).Classes);

        Choice(window, 1).SelectedValue = RewriteAction.Squash;
        AppHarness.Layout(window);
        var classes = row.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("rewriterow")).Classes;
        Assert.Contains("folded", classes);
        Assert.DoesNotContain("dropped", classes);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAltAndAnArrowOrAButtonMoveTheChosenCommit_ShouldMoveItAndKeepItChosen()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var list = Rows(window);
        list.SelectedIndex = 1;
        var moved = model.Rewrite.Rows[1];
        list.Focus();

        window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
        window.KeyRelease(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
        AppHarness.Layout(window);

        Assert.Same(moved, model.Rewrite.Rows[0]);
        Assert.Same(moved, list.SelectedItem);
        window.Descendant<Button>("MoveDownButton")!.Command!.Execute(null);
        AppHarness.Layout(window);
        Assert.Same(moved, model.Rewrite.Rows[1]);
        Assert.Same(moved, list.SelectedItem);
        window.Close();
    }

    private static Control Row(Window window, int row) => (Control)Rows(window).ContainerFromIndex(row)!;
    private static Control Handle(Window window, int row) =>
        Row(window, row).GetVisualDescendants().OfType<Control>().First(control => control.Classes.Contains("grip"));
    private static Point Centre(Control control, Window window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        AppHarness.Layout(window);
    }

    // A row's handle drags its commit; the commit goes where the pointer lets go of it, and stays chosen.
    [AvaloniaFact]
    public async Task WhenARowIsDraggedByItsHandle_ShouldMoveItsCommitWhereItIsLetGo()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var moved = model.Rewrite.Rows[0];

        Drag(window, Centre(Handle(window, 0), window), Centre(Row(window, 1), window));

        Assert.Same(moved, model.Rewrite.Rows[1]);
        Assert.Same(moved, Rows(window).SelectedItem);
        window.Close();
    }

    // Let go over a row in the middle, the commit takes that row's place.
    [AvaloniaFact]
    public async Task WhenARowIsDraggedOntoAnotherRow_ShouldTakeThatRowsPlace()
    {
        var h = Harness();
        var middle = Snapshots.Id('d');
        h.Branches.Rewrites[Older] = new(Snapshots.Id('c'),
            [new(Older, "Older commit", "Older commit", RewriteAction.Pick, "Older commit"),
             new(middle, "Middle commit", "Middle commit", RewriteAction.Pick, "Middle commit"),
             new(Snapshots.HeadId, "Update image", "Update image", RewriteAction.Pick, "Update image")], 0);
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var moved = model.Rewrite.Rows[0];

        Drag(window, Centre(Handle(window, 0), window), Centre(Row(window, 1), window));

        Assert.Equal(["Middle commit", "Older commit", "Update image"], model.Rewrite.Rows.Select(row => row.Subject));
        Assert.Same(moved, model.Rewrite.Rows[1]);
        window.Close();
    }

    // Let go past the last row, the commit goes last.
    [AvaloniaFact]
    public async Task WhenARowIsDraggedBelowTheLastOne_ShouldMoveItsCommitToTheEnd()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var moved = model.Rewrite.Rows[0];
        var below = Centre(Row(window, 1), window) + new Point(0, 120);

        Drag(window, Centre(Handle(window, 0), window), below);

        Assert.Same(moved, model.Rewrite.Rows[^1]);
        window.Close();
    }

    // A long list makes rows only for what it shows: let go below it, the commit goes to the last row shown, not to the end
    // of a thousand commits nobody sees.
    [AvaloniaFact]
    public async Task WhenAThousandCommitsAreListed_ShouldDragARowWithoutJumpingToTheEnd()
    {
        var h = Harness();
        h.Branches.Rewrites[Older] = new(Snapshots.Id('c'),
            [.. Enumerable.Range(0, 1000).Select(index => new RewriteStep(new string('e', 37) + index.ToString("x3"), "Commit " + index, "Commit " + index,
                RewriteAction.Pick, "Commit " + index))], 0);
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var moved = model.Rewrite.Rows[0];
        var shown = Enumerable.Range(0, 1000).Last(index => Rows(window).ContainerFromIndex(index) is not null);
        var list = Rows(window);
        var below = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height + 120), window)!.Value;

        Drag(window, Centre(Handle(window, 0), window), below);

        Assert.True(shown < 100, $"the list made {shown + 1} rows");
        Assert.Equal(shown, model.Rewrite.Rows.IndexOf(moved));
        window.Close();
    }

    // A drag that ends where it began moves nothing, so a joined message written for the group stays.
    [AvaloniaFact]
    public async Task WhenARowIsDraggedWithinItsOwnPlace_ShouldChangeNothing()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        model.Rewrite.Rows[1].Action = RewriteAction.Squash;
        model.Rewrite.SelectedRow = model.Rewrite.Rows[1];
        model.Rewrite.Message = "Joined";
        AppHarness.Layout(window);
        var handle = Centre(Handle(window, 1), window);

        Drag(window, handle, handle + new Point(40, 2));

        Assert.Equal("Joined", model.Rewrite.Message);
        window.Close();
    }

    // Pressed anywhere else, a row is only chosen.
    [AvaloniaFact]
    public async Task WhenARowIsPressedOutsideItsHandle_ShouldNotDragIt()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var first = model.Rewrite.Rows[0];
        var subject = Row(window, 0).GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Older commit");

        Drag(window, Centre(subject, window), Centre(Row(window, 1), window));

        Assert.Same(first, model.Rewrite.Rows[0]);
        window.Close();
    }

    // Once the pointer lets go, moving it moves nothing, nor does a later press elsewhere on a row.
    [AvaloniaFact]
    public async Task WhenTheDragEnds_ShouldMoveNothingMore()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var moved = model.Rewrite.Rows[0];
        var top = Centre(Row(window, 0), window);
        Drag(window, Centre(Handle(window, 0), window), Centre(Row(window, 1), window));

        window.MouseMove(top);
        var subject = Row(window, 1).GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Older commit");
        Drag(window, Centre(subject, window), top);

        Assert.Same(moved, model.Rewrite.Rows[1]);
        window.Close();
    }

    // A drag passes the commit's own place again and again, so the plan keeps turning unchanged and changed; the dialog
    // keeps its height while the line that says so comes and goes, and while the hint below the message does.
    [AvaloniaFact]
    public async Task WhenThePlanTurnsChangedAndBackOrTheMessageTurnsEditable_ShouldKeepTheDialogsHeight()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var content = Rows(window).GetVisualAncestors().OfType<StackPanel>().Last(panel => panel.IsVisible && panel.GetVisualAncestors().Contains(Dialog(window)));
        var unchanged = content.Bounds.Height;

        model.Rewrite.MoveTo(model.Rewrite.Rows[0], 1);
        AppHarness.Layout(window);
        Assert.Equal(unchanged, content.Bounds.Height);

        model.Rewrite.Rows[1].Action = RewriteAction.Reword;
        model.Rewrite.SelectedRow = model.Rewrite.Rows[1];
        AppHarness.Layout(window);
        Assert.Equal(unchanged, content.Bounds.Height);
        window.Close();
    }

    // Thirteen commits scroll; the bar is drawn over the list's content, so the rows end before it instead of under it.
    [AvaloniaFact]
    public async Task WhenTheCommitsScroll_ShouldKeepTheRowsOutOfTheScrollBarsWay()
    {
        var h = Harness();
        h.Branches.Rewrites[Older] = new(Snapshots.Id('c'),
            [.. Enumerable.Range(0, 13).Select(index => new RewriteStep(new string('e', 38) + index.ToString("x2"), "Commit " + index, "Commit " + index,
                RewriteAction.Pick, "Commit " + index))], 0);
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var scroll = Rows(window).GetVisualDescendants().OfType<ScrollViewer>().First();
        var bar = Assert.IsType<double>(window.FindResource("ScrollBarSize"));
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the list scrolls");

        var row = Row(window, 0);
        var end = row.TranslatePoint(new Point(row.Bounds.Width, 0), scroll)!.Value.X;

        Assert.True(end <= scroll.Bounds.Width - bar + 0.5, $"the row ends at {end} of {scroll.Bounds.Width}");
        window.Close();
    }

    // Without Alt an arrow only chooses another commit; the order stays.
    [AvaloniaFact]
    public async Task WhenAnArrowIsPressedWithoutAlt_ShouldChooseTheNextCommitAndKeepTheOrder()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        var first = model.Rewrite.Rows[0];

        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        AppHarness.Layout(window);

        Assert.Same(first, model.Rewrite.Rows[0]);
        Assert.Same(model.Rewrite.Rows[1], Rows(window).SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenARowIsReworded_ShouldLetItsMessageBeWritten()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);

        Choice(window, 0).SelectedValue = RewriteAction.Reword;
        AppHarness.Layout(window);
        var message = window.Descendant<TextBox>("RewriteMessage")!;
        Assert.False(message.IsReadOnly);
        message.Text = "Better message";

        Assert.Equal("Better message", model.Rewrite.Message);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheUpstreamHasTheCommits_ShouldWarnAboveTheButtons()
    {
        var h = Harness();
        h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);

        var warning = window.Descendant<TextBlock>("RewriteWarning")!;
        Assert.True(warning.IsEffectivelyVisible);
        Assert.Equal(RewriteText.Pushed(2, "origin/main"), warning.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheEditIsStarted_ShouldSendItAndClose()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = await OpenedAsync(h, model);
        Choice(window, 1).SelectedValue = RewriteAction.Drop;
        AppHarness.Layout(window);
        var start = window.Descendant<Button>("StartRewriteButton")!;
        Assert.True(start.IsEffectivelyEnabled);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)start.Command!).ExecuteAsync(null);
        AppHarness.Layout(window);

        var request = Assert.IsType<RewriteRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(RewriteAction.Drop, request.Steps[1].Action);
        Assert.False(Dialog(window).IsEffectivelyVisible);
        window.Close();
    }
}
