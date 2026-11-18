using Trag.Desktop.Diagnostics.Preview;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class SyntheticPreviewTests
{
    [Fact]
    public async Task WhenThePreviewIsToggledOnAndOff_ShouldTakeTheSectionsPlaceAndThenReleaseTheListsAndTheSelection()
    {
        await using var model = ShellHarness.Compose();
        var preview = new SyntheticPreview(model);
        Assert.Null(model.CurrentSection);
        preview.Toggle();
        Assert.True(preview.IsShown); Assert.Same(preview, model.CurrentSection); Assert.Equal(10_000, preview.Files.Count); Assert.Equal(50_000, preview.DiffLines.Count);
        Assert.StartsWith("SYNTHETIC DATA", model.StatusBar.Status);
        preview.SelectedFile = (PreviewRow)preview.Files[0];
        preview.Toggle();
        Assert.False(preview.IsShown); Assert.Null(model.CurrentSection); Assert.Empty(preview.Files); Assert.Empty(preview.DiffLines); Assert.Null(preview.SelectedFile);
        Assert.StartsWith("Ready", model.StatusBar.Status);
    }
}
