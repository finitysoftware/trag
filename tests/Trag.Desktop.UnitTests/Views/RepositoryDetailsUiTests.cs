using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Dialogs;
using Trag.Domain.Details;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Views;

// More → Repository details… opens the dialog over the window: the repository by name, folder and branch with the
// folder's actions, the current branch's history, its people, each opening the search, and what the repository holds.
public sealed class RepositoryDetailsUiTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public async Task WhenRepositoryDetailsIsChosenUnderMore_ShouldShowTheRepositoryAndFindAPersonInTheSearch()
    {
        var h = new AppHarness();
        h.History.RepositoryDetails = new(new(3, 5, Day, Day), [new(new("Ana", "ana@example.invalid"), 3), new(new("José", "jose@example.invalid"), 1)],
            new(2, 1, 1, 0, 0, 0, 2048, false, 0));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var more = window.Descendant<Button>("MoreButton")!;
        more.Flyout!.ShowAt(more); AppHarness.Layout(window);
        var item = Assert.IsType<MenuFlyout>(more.Flyout).Items.OfType<MenuItem>().Single(item => item.Name == "RepositoryDetailsMenuItem");
        Assert.Equal(("Repository details", "info"), (item.Header, Assert.IsType<LineIcon>(item.Icon).Kind));

        item.Command!.Execute(null); await model.RepositoryDetails.Details.Completion; AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("RepositoryDetailsOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("Repository details", overlay.Title);
        Assert.Equal(("fixture", "fixture", "main"), (window.Descendant<TextBlock>("DetailsName")!.Text, window.Descendant<TextBlock>("DetailsPath")!.Text,
            window.Descendant<TextBlock>("DetailsBranch")!.Text));
        Assert.Same(model.RepositoryDetails.RevealCommand, window.Descendant<Button>("RevealRepositoryButton")!.Command);
        Assert.Same(model.RepositoryDetails.CopyPathCommand, window.Descendant<Button>("CopyRepositoryPathButton")!.Command);
        Assert.Equal("3 commits on main · 5 on all branches", window.Descendant<TextBlock>("DetailsCommits")!.Text);
        var people = window.Descendant<ItemsControl>("PeopleList")!;
        Assert.Equal(2, people.ItemCount);
        var columns = window.Descendant<ItemsControl>("FactColumns")!;
        Assert.Equal([4, 4], columns.GetVisualDescendants().OfType<ItemsControl>().Select(column => column.ItemCount));

        var ana = people.GetVisualDescendants().OfType<Button>().First();
        ana.Command!.Execute(ana.CommandParameter); AppHarness.Layout(window);

        Assert.False(overlay.IsEffectivelyVisible);
        Assert.Equal((WorkspaceSection.Search, "ana@example.invalid"), (model.Navigator.Section, model.History.SearchQuery));
        window.Close();
    }

    // The history sits beside the repository's name, folder and branch, and the folder's actions beside the folder.
    [AvaloniaFact]
    public async Task WhenTheDetailsShow_ShouldPutTheHistoryBesideTheRepositoryAndTheFoldersActionsBesideIt()
    {
        var h = new AppHarness();
        h.History.RepositoryDetails = new(new(3, 5, Day, Day), [new(new("Ana", "ana@example.invalid"), 3)], RepositoryContents.None);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.RepositoryDetails.OpenCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Rect Placed(string name)
        {
            var control = window.Descendant<Control>(name)!;
            return new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
        }
        var (path, branch, commits, reveal, copy) = (Placed("DetailsPath"), Placed("DetailsBranch"), Placed("DetailsCommits"), Placed("RevealRepositoryButton"), Placed("CopyRepositoryPathButton"));

        Assert.True(commits.Top < branch.Bottom, $"the history starts at {commits.Top}, under the branch at {branch.Bottom}");
        Assert.True(commits.Left > Math.Max(reveal.Right, copy.Right), $"the history starts at {commits.Left}, left of the folder's actions");
        Assert.True(reveal.Top < path.Bottom && copy.Top < path.Bottom, "the folder's actions sit on the folder's line");
        window.Close();
    }

    // While Git answers, a spinner turns in about the room the details will take, so the dialog barely moves when they come.
    [AvaloniaFact]
    public async Task WhenTheDetailsAreBeingRead_ShouldSpinInAboutTheRoomTheyWillTake()
    {
        var h = new AppHarness();
        h.History.RepositoryDetails = new(new(3, 5, Day, Day),
            [.. Enumerable.Range(0, 5).Select(i => new Contributor(new("Person " + i, i + "@example.invalid"), 5 - i))], RepositoryContents.None);
        await using var model = h.Shell();
        var window = h.Window(model); window.Height = 900;
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        h.History.Gate.IsClosed = true;
        var opening = model.RepositoryDetails.OpenCommand.ExecuteAsync(null);
        await h.History.Gate.Started; AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("RepositoryDetailsOverlay")!;
        var card = overlay.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Card");
        var spinner = window.Descendant<Avalonia.Controls.Shapes.Arc>("DetailsSpinner")!;
        var reading = (card.Bounds.Height, spinner.IsEffectivelyVisible, overlay.GetVisualDescendants().OfType<ProgressBar>().Any(bar => bar.IsEffectivelyVisible));

        h.History.Gate.Open(); await opening; AppHarness.Layout(window);

        Assert.Equal((true, false), (reading.Item2, reading.Item3));
        Assert.False(spinner.IsEffectivelyVisible);
        Assert.True(reading.Item1 >= 0.8 * card.Bounds.Height, $"the dialog is {reading.Item1} tall while reading and {card.Bounds.Height} once read");
        window.Close();
    }

    // The people scroll across a gutter as wide as the scroll bar, so the bar lies beside their commit counts.
    [AvaloniaFact]
    public async Task WhenThePeopleScroll_ShouldKeepThemOutOfTheScrollBarsWay()
    {
        var h = new AppHarness();
        h.History.RepositoryDetails = new(new(3, 5, Day, Day),
            [.. Enumerable.Range(0, 12).Select(i => new Contributor(new("Person " + i, i + "@example.invalid"), 12 - i))], RepositoryContents.None);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.RepositoryDetails.OpenCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var people = window.Descendant<ItemsControl>("PeopleList")!;
        var scroll = people.FindAncestorOfType<ScrollViewer>()!;
        var bar = Assert.IsType<double>(window.FindResource("ScrollBarSize"));

        var end = people.TranslatePoint(new Point(people.Bounds.Width, 0), scroll)!.Value.X;

        Assert.True(end <= scroll.Bounds.Width - bar + 0.5, $"the people end at {end} of {scroll.Bounds.Width}");
        window.Close();
    }
}
