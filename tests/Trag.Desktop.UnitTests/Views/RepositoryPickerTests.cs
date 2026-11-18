using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Views;
using Trag.Desktop.Views.Repositories;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

public sealed class RepositoryPickerTests
{

    [AvaloniaFact]
    public async Task WhenThePickerOpens_ShouldReplaceTheWorkspaceSwapTheToolbarAndOpenTheSelectedEntry()
    {
        var alpha = Path.Combine(Path.GetTempPath(), "trag-picker-tests", "alpha");
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "abc", identity: false);
        h.Catalog.Entries.Add(new(alpha, "alpha", DateTimeOffset.UtcNow));
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        Assert.Empty(window.GetVisualDescendants().OfType<RepositoriesView>());
        Assert.True(window.Descendant<Button>("OpenButton")!.IsEffectivelyVisible);
        model.OpenCommand.Execute(null); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = Assert.Single(window.GetVisualDescendants().OfType<RepositoriesView>());
        Assert.False(window.Descendant<Button>("OpenButton")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("NewRepositoryButton")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("BackButton")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("PickerOpenButton")!.IsEffectivelyEnabled);
        Assert.False(window.Descendant<Button>("DeleteRepositoryButton")!.IsEffectivelyEnabled);
        var list = view.Descendant<ListBox>("LocalRepositoriesList")!;
        Assert.Equal(1, list.ItemCount);
        list.SelectedIndex = 0; AppHarness.Layout(window);
        Assert.Equal("alpha", view.Descendant<TextBlock>("DetailsName")!.Text);
        Assert.True(window.Descendant<Button>("PickerOpenButton")!.IsEffectivelyEnabled);
        Assert.True(window.Descendant<Button>("DeleteRepositoryButton")!.IsEffectivelyEnabled);
        window.Descendant<Button>("PickerOpenButton")!.Command!.Execute(null);
        await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        Assert.True(model.Repository.HasRepository); Assert.Equal(alpha, model.Repository.Snapshot!.Root);
        Assert.Empty(window.GetVisualDescendants().OfType<RepositoriesView>());
        Assert.True(window.Descendant<Button>("OpenButton")!.IsEffectivelyVisible);
        model.OpenCommand.Execute(null); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Button>("BackButton")!.IsEffectivelyVisible);
        window.Close();
    }
}
