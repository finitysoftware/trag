using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The commit list reads the next page of the history as it is scrolled near its end, and only then.
public sealed class HistoryListTests
{
    [AvaloniaFact]
    public async Task WhenTheListIsScrolledToItsEnd_ShouldReadTheNextPageBelowAndNotBefore()
    {
        var h = new AppHarness();
        h.History.Pages = (_, page) => new([.. Enumerable.Range(page * 100, 100).Select(n => Snapshots.Commit(n.ToString("x40"), "Commit " + n))], true, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("HistoryList")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(1, h.History.PageCalls);

        list.Offset = new Vector(0, list.Extent.Height - list.Viewport.Height);
        AppHarness.Layout(window); await model.History.HistoryCompletion;

        Assert.Equal((2, 200), (h.History.PageCalls, model.History.Page.Commits.Count));
        window.Close();
    }
}
