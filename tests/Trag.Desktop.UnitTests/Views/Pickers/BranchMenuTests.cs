using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Views.Pickers;
using Trag.TestSupport;
using Trag.ViewModels.Branches;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views.Pickers;

// A branch's actions are one menu behind the … of its row, shown while the pointer is over the row: renaming it, merging
// it into the current branch, its pull or merge request on GitHub or GitLab, and deleting it. A right-click opens nothing.
public sealed class BranchMenuTests
{
    private static async Task<(AppHarness Harness, ShellViewModel Model, Window Window, BranchPicker Picker)> PickerAsync(string address = "https://github.com/finity/trag.git")
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", address)], trackingRemote: "origin", upstream: "origin/main");
        h.Branches.Local.Add("topic");
        h.Branches.Remote.Add("origin/release");
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.Completion;
        var picker = new BranchPicker { DataContext = model.Branches };
        var window = new Window { Content = picker, Width = 400, Height = 500 };
        window.Show();
        AppHarness.Layout(window);
        return (h, model, window, picker);
    }

    private static Button RowOf(BranchPicker picker, BranchRow row) =>
        picker.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("branch") && ReferenceEquals(button.CommandParameter, row));

    private static Button MoreOf(Button row) => row.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("rowmore"));

    // What the menu offers: the items it shows.
    private static (string? Header, string? Icon, object? Command, object? Parameter)[] Items(MenuFlyout menu) =>
        [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => ((string?)item.Header, (item.Icon as LineIcon)?.Kind, (object?)item.Command, item.CommandParameter))];

    [AvaloniaFact]
    public async Task WhenALocalBranchsMenuOpens_ShouldOfferMergePullRequestAndDeleteForThatBranch()
    {
        var (h, model, window, picker) = await PickerAsync();
        await using var _ = model;
        var topic = model.Branches.Rows.Local.Single();
        var branches = model.Branches;

        var more = MoreOf(RowOf(picker, topic));
        var menu = Open(window, more);
        window.MouseMove(new Point(1, 1));
        AppHarness.Layout(window);

        Assert.Equal(1, more.Opacity);

        Assert.Equal([("Rename", "edit", branches.RequestRenameCommand, topic), ("Merge " + topic.Branch.Name + " into the current branch", "merge", branches.RequestMergeCommand, topic),
                ("Create pull request", "github", branches.CreatePullRequestCommand, topic),
                ("Delete branch", "trash", branches.RequestDeleteCommand, topic)], Items(menu));
        Assert.Single(menu.Items.OfType<Separator>());
        Assert.Contains("danger", menu.Items.OfType<MenuItem>().Last().Classes);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheCurrentOrARemoteBranchsMenuOpens_ShouldOfferOnlyWhatFitsIt()
    {
        var (_, model, window, picker) = await PickerAsync("git@gitlab.com:finity/trag.git");
        await using var __ = model;
        var (current, release) = (model.Branches.Rows.Current!, model.Branches.Rows.Remote.Single());
        var branches = model.Branches;

        var currentMenu = Open(window, MoreOf(picker.Descendant<Button>("CurrentBranchButton")!));
        Assert.Equal([("Rename", "edit", branches.RequestRenameCommand, current), ("Create merge request", "gitlab", branches.CreatePullRequestCommand, current)], Items(currentMenu));
        currentMenu.Hide();

        Assert.Equal([("Merge " + release.Branch.Name + " into the current branch", "merge", branches.RequestMergeCommand, release),
                ("Delete remote branch", "trash", branches.RequestDeleteCommand, release)], Items(Open(window, MoreOf(RowOf(picker, release)))));
        window.Close();
    }

    // The … shows while the pointer is over its row; the row's own icons and its right-click menu are gone.
    [AvaloniaFact]
    public async Task WhenThePointerComesOverARow_ShouldShowItsMoreButtonAndNothingElse()
    {
        var (_, model, window, picker) = await PickerAsync();
        await using var _ = model;
        var row = RowOf(picker, model.Branches.Rows.Local.Single());
        var more = MoreOf(row);

        Assert.Equal(0, more.Opacity);
        window.MouseMove(row.TranslatePoint(new Point(10, row.Bounds.Height / 2), window)!.Value);
        AppHarness.Layout(window);

        Assert.Equal(1, more.Opacity);
        Assert.Equal(("Branch actions", "Branch actions"), (ToolTip.GetTip(more), AutomationProperties.GetName(more)));
        Assert.All(picker.GetVisualDescendants().OfType<Button>(), button => Assert.False(button.Classes.Contains("trash") || button.Classes.Contains("rowaction")));
        Assert.All(picker.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("branch")), button => Assert.Null(button.ContextFlyout));
        window.Close();
    }

    // The menu opens beside the …, to its right, over the history: never back across the list the row belongs to. The
    // window leaves room on both sides of the picker, so the menu could fit either way, and Avalonia does not move it.
    [AvaloniaFact]
    public async Task WhenABranchsMenuOpens_ShouldOpenToTheRightOfItsButton()
    {
        var (_, model, window, picker) = await PickerAsync();
        await using var __ = model;
        (window.Width, picker.Width, picker.Margin, picker.HorizontalAlignment) = (1300, 300, new Thickness(500, 0, 0, 0), Avalonia.Layout.HorizontalAlignment.Left);
        AppHarness.Layout(window);
        var current = picker.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CurrentBranchButton");

        foreach (var more in new[] { MoreOf(current), MoreOf(RowOf(picker, model.Branches.Rows.Local.Single())), MoreOf(RowOf(picker, model.Branches.Rows.Remote.Single())) })
        {
            var menu = Open(window, more);
            var opened = menu.Items.OfType<MenuItem>().First().GetVisualAncestors().OfType<MenuFlyoutPresenter>().First();
            Assert.True(opened.PointToScreen(new Point()).X >= more.PointToScreen(new Point()).X, "The menu opened to the left of its button.");
            menu.Hide();
            AppHarness.Layout(window);
        }
        window.Close();
    }

    private static MenuFlyout Open(Window window, Button more)
    {
        var point = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        AppHarness.Layout(window);
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.True(menu.IsOpen);
        return menu;
    }
}
