using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Trag.Desktop.UnitTests.Views;

public sealed class SubmoduleUiTests
{
    // The file status says when submodules are not at the commits the branch records, beside the update that fixes it.
    [AvaloniaFact]
    public async Task WhenSubmodulesAreNotAtTheRecordedCommits_ShouldShowTheNoticeWithItsUpdate()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var banner = window.Descendant<Border>("SubmoduleBanner")!;
        Assert.False(banner.IsEffectivelyVisible);

        h.Reader.Snapshot = h.Reader.Snapshot with { SubmodulesToUpdate = ["sub"] };
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);

        Assert.True(banner.IsEffectivelyVisible);
        Assert.Contains(banner.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.FileStatus.SubmoduleNotice);
        Assert.Same(model.FileStatus.UpdateSubmodulesCommand, window.Descendant<Button>("UpdateSubmodulesButton")!.Command);
        window.Close();
    }

    // In a submodule the sidebar leads back to the repository it belongs to; elsewhere it shows no such way.
    [AvaloniaFact]
    public async Task WhenTheRepositoryIsASubmodule_ShouldOfferTheWayBackInTheSidebar()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var back = window.Descendant<Button>("SuperprojectButton")!;
        Assert.False(back.IsEffectivelyVisible);

        h.Reader.Snapshot = h.Reader.Snapshot with { Superproject = "super" };
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);

        Assert.True(back.IsEffectivelyVisible);
        Assert.Same(model.OpenSuperprojectCommand, back.Command);
        Assert.Contains(back.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Back to super");
        window.Close();
    }
}
