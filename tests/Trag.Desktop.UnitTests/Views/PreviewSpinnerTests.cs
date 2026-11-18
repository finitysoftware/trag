using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// While Git reads what a merge or a rebase would do, a spinner turns in the middle of the preview card, as it does in the
// repository details, instead of a bar under it, in a room as tall as the graph that comes in its place once the preview
// is read.
public sealed class PreviewSpinnerTests
{
    [AvaloniaFact]
    public async Task WhenTheMergePreviewIsBeingRead_ShouldSpinInTheMiddleOfTheCardWhereTheGraphWillBe()
    {
        var h = new AppHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = Snapshots.Id('b');
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.MergeDialog.OpenCommand.ExecuteAsync(null); AppHarness.Layout(window);
        h.Branches.PreviewGate.IsClosed = true;

        window.Descendant<ComboBox>("MergeFromSelector")!.SelectedItem = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "feature");
        await h.Branches.PreviewGate.Started; AppHarness.Layout(window);

        var spinner = window.Descendant<Arc>("MergePreviewSpinner")!;
        AssertSpinsInTheMiddle(window.Descendant<Border>("MergePreviewCard")!, spinner);
        var room = ((Control)spinner.Parent!).Bounds.Height;
        h.Branches.PreviewGate.Open(); await model.MergeDialog.Merge.Preview.Completion; AppHarness.Layout(window);
        Assert.False(spinner.IsEffectivelyVisible);
        Assert.Equal(window.Descendant<Canvas>("FastForwardGraph")!.Bounds.Height, room);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheRebasePreviewIsBeingRead_ShouldSpinInTheMiddleOfTheCardWhereTheGraphWillBe()
    {
        var h = new AppHarness();
        h.Branches.Local.Add("feature");
        h.Branches.Heads["feature"] = Snapshots.Id('b');
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        await model.MergeDialog.ShowRebaseCommand.ExecuteAsync(null); AppHarness.Layout(window);
        h.Branches.PreviewGate.IsClosed = true;

        window.Descendant<ComboBox>("RebaseOntoSelector")!.SelectedItem = model.MergeDialog.Rebase.Draft.OntoOptions.Single(row => row.Name == "feature");
        await h.Branches.PreviewGate.Started; AppHarness.Layout(window);

        var spinner = window.Descendant<Arc>("RebasePreviewSpinner")!;
        AssertSpinsInTheMiddle(window.Descendant<Border>("RebasePreviewCard")!, spinner);
        var room = ((Control)spinner.Parent!).Bounds.Height;
        h.Branches.PreviewGate.Open(); await model.MergeDialog.Rebase.Preview.Completion; AppHarness.Layout(window);
        Assert.False(spinner.IsEffectivelyVisible);
        Assert.Equal(window.Descendant<Canvas>("RebaseGraph")!.Bounds.Height, room);
        window.Close();
    }

    private static void AssertSpinsInTheMiddle(Border card, Arc spinner)
    {
        Assert.True(spinner.IsEffectivelyVisible);
        Assert.Contains("spinning", spinner.Classes);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsEffectivelyVisible);
        var middle = spinner.TranslatePoint(new Point(spinner.Bounds.Width / 2, spinner.Bounds.Height / 2), card)!.Value;
        Assert.True(Math.Abs(middle.X - card.Bounds.Width / 2) <= 1 && Math.Abs(middle.Y - card.Bounds.Height / 2) <= 1,
            $"the spinner turns at {middle} of a card {card.Bounds.Size}");
    }
}
