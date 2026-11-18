using Trag.Desktop.Diagnostics.Preview;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class PreviewTests
{
    [Fact]
    public void WhenALargePreviewIsShown_ShouldCreateOnlyTheRowsAskedFor()
    {
        var created = 0;
        var list = new PreviewList(100_000, i => { created++; return new(i + 1, "sample"); });
        Assert.Equal(0, created);
        Assert.Equal(100_000, ((PreviewRow)list[99_999]).Number);
        Assert.Equal(1, created);
    }
}
