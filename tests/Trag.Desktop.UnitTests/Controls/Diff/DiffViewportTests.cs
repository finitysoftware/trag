using Avalonia.Input;
using Trag.Application.Diff;
using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Controls.Diff;

public sealed class DiffViewportTests
{
    private static Task<DiffPresentation> Build(params PatchLine[] lines) =>
        new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "sample.cs", default);

    [Fact]
    public async Task WhenLineNumbersHaveSevenDigits_ShouldFitTheGutterToThemInEitherMode()
    {
        var document = await Build(new PatchLine(' ', " context", 1_000_001, 1_000_002));
        Assert.Equal(7, document.NumberDigits);
        Assert.Equal(7 * 14 + 16, new DiffViewport(document, false).GutterWidth);
        Assert.Equal(7 * 7 + 8, new DiffViewport(document, true).GutterWidth);
    }

    [Fact]
    public async Task WhenRowsAreMapped_ShouldLeadToTheUnifiedLinesAndBackInBothModes()
    {
        var document = await Build(new PatchLine('-', "-old 1", 1), new PatchLine('+', "+new 1", null, 1), new PatchLine(' ', " same", 2, 2));
        var unified = new DiffViewport(document, false);
        Assert.Equal(3, unified.RowCount); Assert.Equal(2, unified.ToSource(2)); Assert.Equal(2, unified.ToRow(2));
        var split = new DiffViewport(document, true);
        Assert.Equal(document.SplitRows.Count, split.RowCount);
        for (var source = 0; source < 3; source++) Assert.Equal(document.ToUnified(document.ToSplit(source)), split.ToSource(split.ToRow(source)));
        Assert.Equal(unified.ToSource(DiffViewport.TopRow(2 * DiffViewport.RowHeight + 5)), unified.TopSource(2 * DiffViewport.RowHeight + 5));
    }

    [Theory]
    [InlineData(600, 26)] [InlineData(22, 1)] [InlineData(0, 1)]
    public void WhenTheViewHasAHeight_ShouldPageByTheRowsThatFitLessOne(double height, int expected) => Assert.Equal(expected, DiffViewport.PageSize(height));

    [Theory]
    [InlineData(Key.Up, 5, 4)] [InlineData(Key.Down, 5, 6)] [InlineData(Key.PageUp, 30, 10)] [InlineData(Key.PageDown, 30, 50)] [InlineData(Key.Home, 30, 0)] [InlineData(Key.End, 30, 99)]
    public void WhenANavigationKeyIsPressed_ShouldMoveTheSelection(Key key, int selected, int expected) => Assert.Equal(expected, DiffViewport.KeyTarget(key, selected, 100, 20));

    [Fact]
    public void WhenAnotherKeyIsPressed_ShouldNotNavigate() => Assert.Null(DiffViewport.KeyTarget(Key.A, 5, 100, 20));

    [Fact]
    public async Task WhenALineIsWiderThanItsSide_ShouldScrollOverTheColumnsBeyondIt()
    {
        var document = await Build(new PatchLine('+', "+" + new string('x', 199), null, 1));
        Assert.Equal(200, document.MaxColumns);
        var unified = new DiffViewport(document, false);
        // The range is the shown columns in cells as wide as the row draws them.
        var (viewport, maximum) = unified.HorizontalRange(rowWidth: 400, controlWidth: 900, splitRatio: 0.5, cellWidth: 7.2);
        Assert.Equal(400 - unified.GutterWidth - 6, viewport); Assert.Equal(200 * 7.2 + 24 - viewport, maximum);
        var split = new DiffViewport(document, true);
        var (sideViewport, _) = split.HorizontalRange(rowWidth: 405, controlWidth: 900, splitRatio: 0.25, cellWidth: 7.2);
        Assert.Equal(400 * 0.25 - split.GutterWidth - 6, sideViewport);
        // Before any row is laid out the control width stands in; a short text never scrolls.
        var narrow = await Build(new PatchLine('+', "+x", null, 1));
        var (fallbackViewport, none) = new DiffViewport(narrow, false).HorizontalRange(rowWidth: 0, controlWidth: 916, splitRatio: 0.5, cellWidth: 7.2);
        Assert.Equal(900 - new DiffViewport(narrow, false).GutterWidth - 6, fallbackViewport); Assert.Equal(0, none);
    }

    // a, then b and c replaced by B, then d: the block is lines 1 to 3, three unified rows, two split rows (b beside B, c
    // alone) after a's row.
    private static Task<DiffPresentation> Replaced() => Build(new PatchLine(' ', " a", 1, 1), new PatchLine('-', "-b", 2),
        new PatchLine('-', "-c", 3), new PatchLine('+', "+B", null, 2), new PatchLine(' ', " d", 4, 3));

    [Fact]
    public async Task WhenABlockIsInView_ShouldPutItsActionBesideItsFirstRowInEitherMode()
    {
        var document = await Replaced();
        var block = new DiffBlock(1, 3);

        Assert.Equal(DiffViewport.RowHeight, new DiffViewport(document, false).ActionTop(block, verticalOffset: 0, height: 600));
        Assert.Equal(DiffViewport.RowHeight - 10, new DiffViewport(document, true).ActionTop(block, verticalOffset: 10, height: 600));
    }

    // Scrolled into the block, the action stays on the first row still in view, down to the block's last row.
    [Theory]
    [InlineData(false, 2.5, 0.0)] [InlineData(false, 3.9, 0.0)] [InlineData(false, 4.0, null)] [InlineData(true, 2.9, 0.0)] [InlineData(true, 3.0, null)]
    public async Task WhenTheViewIsScrolledIntoOrPastABlock_ShouldKeepItsActionOnTheTopRowUntilTheBlockLeaves(bool split, double rows, double? expected)
    {
        var document = await Replaced();

        Assert.Equal(expected, new DiffViewport(document, split).ActionTop(new DiffBlock(1, 3), rows * DiffViewport.RowHeight, height: 600));
    }

    // Chosen rows show changed lines, in order; a split row shows both sides of its pair, and an unchanged line is no change.
    [Fact]
    public async Task WhenRowsAreChosen_ShouldLeadToTheChangedLinesTheyShowInEitherMode()
    {
        var document = await Build(new PatchLine(' ', " a", 1, 1), new PatchLine('-', "-b", 2), new PatchLine('+', "+B", null, 2),
            new PatchLine(' ', " c", 3, 3), new PatchLine('-', "-d", 4), new PatchLine('+', "+D", null, 4));

        Assert.Equal([1, 4], new DiffViewport(document, false).ChangesIn([4, 0, 1]));
        Assert.Equal([1, 2, 4, 5], new DiffViewport(document, true).ChangesIn([3, 1, 0]));
    }

    [Fact]
    public async Task WhenABlockIsBelowTheView_ShouldPutNoActionBesideIt()
    {
        var document = await Replaced();

        Assert.Null(new DiffViewport(document, false).ActionTop(new DiffBlock(1, 3), verticalOffset: 0, height: DiffViewport.RowHeight));
    }
}
