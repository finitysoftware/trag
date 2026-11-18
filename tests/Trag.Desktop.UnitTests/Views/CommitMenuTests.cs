using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Views.History;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views;

// The actions of a commit as a menu: a right-click on a commit of the history opens it with the commit named at the top,
// and the … button of the commit pane opens it without the name, which the pane shows already.
public sealed class CommitMenuTests
{
    private static readonly string Older = Snapshots.Id('b');

    [AvaloniaFact]
    public async Task WhenACommitIsRightClicked_ShouldChooseItAndOfferTheCommitsActionsWithTheirIcons()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var row = await HistoryRowAsync(h, model, window, 1);

        var menu = await RightClickAsync(model, window, row);

        Assert.Equal(Older, model.History.SelectedCommit?.Id);
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal([Older[..10] + " · Older commit", "Create branch", "Create tag", "Revert", "Cherry-pick", "Reset current branch to here", "Edit commits from here", "Copy SHA"],
            items.Select(item => item.Header as string ?? (item.Header as TextBlock)?.Text));
        Assert.False(items[0].IsHitTestVisible);
        Assert.Equal(["branch", "tag", "discard", "cherrypick", "reset", "rebase", "link"], items.Skip(1).Select(item => Assert.IsType<LineIcon>(item.Icon).Kind));
        Assert.Equal(4, menu.Items.OfType<Separator>().Count());
        Assert.Contains("actions", menu.FlyoutPresenterClasses);
        window.Close();
    }

    // Without the mouse: the menu key on a commit row with focus chooses that commit, as a right-click does, and opens its menu.
    [AvaloniaFact]
    public async Task WhenAFocusedCommitRowGetsTheMenuKey_ShouldChooseItAndOpenItsMenu()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var row = await HistoryRowAsync(h, model, window, 1);
        row.Focus();
        AppHarness.Layout(window);

        window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        AppHarness.Layout(window); await model.History.Details.Completion; AppHarness.Layout(window);

        Assert.Equal(Older, model.History.SelectedCommit?.Id);
        Assert.True(Assert.IsType<MenuFlyout>(row.GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout).IsOpen);
        window.Close();
    }

    // One … button instead of an icon per action.
    [AvaloniaFact]
    public async Task WhenTheMoreButtonOfTheCommitPaneIsClicked_ShouldOfferTheCommitsActionsWithoutTheCommitsName()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await HistoryRowAsync(h, model, window, 1);

        var menu = await MoreAsync(model, window, 1);

        var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
        var more = history.Descendant<Button>("CommitActionsButton")!;
        Assert.Equal("more", Assert.IsType<LineIcon>(more.Content).Kind);
        Assert.Equal(("Commit actions", "Commit actions"), (ToolTip.GetTip(more), AutomationProperties.GetName(more)));
        Assert.All(["CreateBranchFromCommitButton", "CreateTagFromCommitButton", "RevertCommitButton", "CherryPickCommitButton"],
            name => Assert.Null(history.Descendant<Button>(name)));
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["Create branch", "Create tag", "Revert", "Cherry-pick", "Reset current branch to here", "Edit commits from here", "Copy SHA"], items.Select(item => item.Header as string));
        Assert.Equal(["branch", "tag", "discard", "cherrypick", "reset", "rebase", "link"], items.Select(item => Assert.IsType<LineIcon>(item.Icon).Kind));
        Assert.Equal(3, menu.Items.OfType<Separator>().Count());
        Assert.Contains("actions", menu.FlyoutPresenterClasses);
        window.Close();
    }

    // Each action opens its dialog for the commit the menu was opened on, whichever way it was opened, and Copy SHA copies
    // that commit's full ID.
    [AvaloniaTheory]
    [InlineData("row")]
    [InlineData("pane")]
    public async Task WhenAnActionOfTheMenuIsChosen_ShouldOpenItsDialogForThatCommit(string opener)
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var row = await HistoryRowAsync(h, model, window, 1);
        var menu = opener == "row" ? await RightClickAsync(model, window, row) : await MoreAsync(model, window, 1);
        var actions = menu.Items.OfType<MenuItem>().Where(item => !item.Classes.Contains("caption")).ToArray();

        actions[1].Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(model.Tags.Draft.IsOpen); Assert.Equal(Older, model.Tags.Draft.Commit?.Id);
        model.Tags.CancelCreateTagCommand.Execute(null);
        actions[2].Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(model.Revert.Question.IsOpen); Assert.Equal(Older, model.Revert.Question.Subject?.Id);
        model.Revert.Question.CancelCommand.Execute(null);
        await RunAsync(actions[3]);
        Assert.True(model.CherryPick.Question.IsOpen); Assert.Equal(Older, model.CherryPick.Question.Subject?.Id);
        model.CherryPick.Question.CancelCommand.Execute(null);
        await RunAsync(actions[4]);
        Assert.True(model.Reset.Question.IsOpen); Assert.Equal(Older, model.Reset.Question.Subject?.Id);
        model.Reset.Question.CancelCommand.Execute(null);
        await RunAsync(actions[5]);
        Assert.True(model.Rewrite.Question.IsOpen);
        model.Rewrite.Question.CancelCommand.Execute(null);
        await RunAsync(actions[0]);
        Assert.True(model.Branches.Draft.IsOpen); Assert.Equal(Older, model.Branches.Draft.SelectedStartPoint?.Ref);
        var host = new RecordingHost();
        model.Host = host;
        actions[6].Command!.Execute(null);
        Assert.Equal([("copy", Older)], host.Calls);
        window.Close();
    }

    // An item's command run to its end: the async ones are awaited.
    private static Task RunAsync(MenuItem item)
    {
        if (item.Command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command) return command.ExecuteAsync(null);
        item.Command!.Execute(null);
        return Task.CompletedTask;
    }

    // History with two commits, shown, and the row at the index.
    private static async Task<ListBoxItem> HistoryRowAsync(AppHarness h, ShellViewModel model, Window window, int index)
    {
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        return (ListBoxItem)window.Descendant<ListBox>("HistoryList")!.ContainerFromIndex(index)!;
    }

    // A right-click in the middle of the row, as the mouse does it; answers the menu it opened.
    private static async Task<MenuFlyout> RightClickAsync(ShellViewModel model, Window window, ListBoxItem row)
    {
        Click(window, row, MouseButton.Right);
        AppHarness.Layout(window); await model.History.Details.Completion; AppHarness.Layout(window);
        return Assert.IsType<MenuFlyout>(row.GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout);
    }

    // The commit at the index chosen and the … button of its pane clicked, as the mouse does it; answers the button's menu.
    private static async Task<MenuFlyout> MoreAsync(ShellViewModel model, Window window, int index)
    {
        model.History.SelectedCommit = model.History.Page.Commits[index]; await model.History.Details.Completion; AppHarness.Layout(window);
        var more = window.Descendant<Button>("CommitActionsButton")!;
        Click(window, more, MouseButton.Left);
        AppHarness.Layout(window);
        return Assert.IsType<MenuFlyout>(more.Flyout);
    }

    private static void Click(Window window, Control control, MouseButton button)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, button); window.MouseUp(point, button);
    }
}
