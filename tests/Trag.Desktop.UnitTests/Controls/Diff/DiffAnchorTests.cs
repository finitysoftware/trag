using Trag.Application.Diff;
using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Controls.Diff;

public sealed class DiffAnchorTests
{
    private const double Row = DiffViewport.RowHeight;

    [Fact]
    public async Task WhenTheModeChangesOrTheContentIsReadAgain_ShouldPlaceTheViewOnTheSameLines()
    {
        var document = await Build(changedLast: false);
        var unified = new DiffViewport(document, false);
        var anchor = DiffAnchor.Capture(unified, 801 * Row + 5, 803)!;
        Assert.Equal(document.Lines[801], anchor.Top);
        Assert.Equal(document.Lines[803], anchor.Selected);
        Assert.Equal(5, anchor.Fraction);

        var split = anchor.Locate(new DiffViewport(document, true));
        Assert.Equal(document.ToSplit(801) * Row + 5, split.VerticalOffset);
        Assert.Equal(803, split.Selected);

        // The re-read dropped its first line, so the same lines sit one index earlier.
        var reread = await Build(changedLast: true);
        var again = anchor.Locate(new DiffViewport(reread, false));
        Assert.Equal(800 * Row + 5, again.VerticalOffset);
        Assert.Equal(802, again.Selected);
    }

    [Fact]
    public async Task WhenTheAnchoredLinesAreGone_ShouldLeaveNoSelectionAndStartAtTheTop()
    {
        var document = await Build(changedLast: false);
        var anchor = DiffAnchor.Capture(new DiffViewport(document, false), 1_998 * Row, 1_999)!;
        var other = await new BackgroundDiffBuilder().BuildAsync(new([], [new('+', "+elsewhere", null, 1)], DiffContentKind.Text, ""), "b.txt", TestContext.Current.CancellationToken);
        Assert.Equal(DiffPlacement.Start, anchor.Locate(new DiffViewport(other, false)));

        // The top line is still there, one index earlier; the selected last line was changed and is not.
        var reread = await Build(changedLast: true);
        var placed = anchor.Locate(new DiffViewport(reread, false));
        Assert.Equal(1_997 * Row, placed.VerticalOffset);
        Assert.Equal(-1, placed.Selected);
    }

    [Fact]
    public void WhenThereAreNoRows_ShouldCaptureNoAnchor() =>
        Assert.Null(DiffAnchor.Capture(new DiffViewport(DiffPresentation.Empty, false), 0, -1));

    private static Task<DiffPresentation> Build(bool changedLast)
    {
        var lines = new List<PatchLine>();
        for (var number = 1; number <= 1_000; number++)
        {
            lines.Add(new('-', "-old " + number, number));
            lines.Add(new('+', changedLast && number == 1_000 ? "+changed" : "+new " + number, null, number));
        }
        // The re-read drops the first line, so every line sits at a new index.
        if (changedLast) lines.RemoveAt(0);
        return new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "a.txt", default);
    }
}
