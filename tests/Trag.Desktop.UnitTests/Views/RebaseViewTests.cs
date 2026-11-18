using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Views.Rebase;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The rebase view in the window: created while its section shows, the plan over the shared conflict panels with the sides
// named yours and upstream, the footer's commands, and the sidebar's step.
public sealed class RebaseViewTests
{
    [AvaloniaFact]
    public async Task WhenARebaseStops_ShouldShowThePlanTheSidesByTheirNamesAndTheStepInTheSidebar()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Rebasing(Snapshots.Rebase(step: 2, total: 3), [Snapshots.Conflict("f.cs")])
            with { Resolutions = new([], [new(Snapshots.Path("f.cs"), 1)]) };
        await using var model = h.Shell();
        var window = h.Window(model);
        Assert.Empty(window.GetVisualDescendants().OfType<RebaseView>());
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);

        var view = Assert.Single(window.GetVisualDescendants().OfType<RebaseView>());
        Assert.Equal(3, view.Descendant<ItemsControl>("RebaseSteps")!.GetRealizedContainers().Count());
        Assert.Equal("Use yours", view.Descendant<Button>("UseYoursButton")!.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("Use upstream", view.Descendant<Button>("UseOtherButton")!.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("Commit 2", view.Descendant<TextBox>("RebaseMessageEditor")!.Text);
        Assert.Same(model.Rebase.ContinueRebaseCommand, view.Descendant<Button>("ContinueRebaseButton")!.Command);
        Assert.False(view.Descendant<Button>("ContinueRebaseButton")!.IsEffectivelyEnabled);
        Assert.True(view.Descendant<Button>("SkipCommitButton")!.IsEffectivelyEnabled);
        Assert.True(window.Descendant<Button>("RebaseButton")!.IsEffectivelyVisible);
        Assert.Equal("2/3", window.Descendant<Border>("RebaseStepCount")!.GetVisualDescendants().OfType<TextBlock>().Single().Text);

        model.Navigator.ShowHistoryCommand.Execute(null); AppHarness.Layout(window);
        Assert.Empty(window.GetVisualDescendants().OfType<RebaseView>());
        window.Close();
    }
}
