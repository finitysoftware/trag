using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Trag.Desktop.UnitTests.Views;

// The Cancel a history read offers sits in the header as a small ghost button, so the header keeps its height while it
// shows.
public sealed class HistoryHeaderTests
{
    [AvaloniaFact]
    public async Task WhenAHistoryReadOffersCancel_ShouldKeepTheHeaderAsTallAsWithoutIt()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        h.History.Gate.IsClosed = true;
        model.Navigator.ShowHistoryCommand.Execute(null); await h.History.Gate.Started; AppHarness.Layout(window);
        var cancel = window.Descendant<Button>("HistoryCancelButton")!;
        var header = cancel.FindAncestorOfType<Border>()!;
        var reading = (cancel.IsEffectivelyVisible, header.Bounds.Height);

        h.History.Gate.Open(); await model.History.HistoryCompletion; AppHarness.Layout(window);

        Assert.True(reading.IsEffectivelyVisible);
        Assert.False(cancel.IsEffectivelyVisible);
        Assert.Equal(header.Bounds.Height, reading.Height);
        window.Close();
    }
}
