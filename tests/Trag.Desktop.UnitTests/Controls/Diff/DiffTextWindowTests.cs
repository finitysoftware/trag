using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Controls.Diff;

public sealed class DiffTextWindowTests
{
    [Fact]
    public void WhenALineHasTabsACarriageReturnAndNoNewline_ShouldShowThemAsMarkers()
    {
        var line = new PatchLine('+', "+a\tb\rc") { HasCarriageReturn = true, NoNewline = true };
        var window = DiffTextWindow.Create(line, 0, 1000, 1, lineEndings: true);
        Assert.Equal("+a→   b␍c  · CRLF  · No newline at end of file", window.VisibleText);
    }

    // In a diff whose lines all end the same way the CRLF mark is left out; a missing last newline is still said.
    [Fact]
    public void WhenTheDiffDoesNotMarkLineEndings_ShouldLeaveOutTheCrlfMarkButKeepTheMissingNewline()
    {
        var line = new PatchLine('+', "+code") { HasCarriageReturn = true, NoNewline = true };
        Assert.Equal("+code  · No newline at end of file", DiffTextWindow.Create(line, 0, 1000, 1, lineEndings: false).VisibleText);
    }

    [Fact]
    public void WhenAWindowEdgeFallsInsideASurrogatePair_ShouldKeepThePairWhole()
    {
        var line = new PatchLine('+', "ab😀cdef😀ghijklmnop");
        var left = DiffTextWindow.Create(line, 5, 0, 1, false);
        Assert.Equal(2, left.Start);
        Assert.StartsWith("😀", left.VisibleText);
        var right = DiffTextWindow.Create(line, 0, 1, 1, false);
        Assert.Equal("ab😀cdef", right.VisibleText);
    }

    [Fact]
    public void WhenAVeryLongLineFillsAVeryWideWindow_ShouldShowAtMost1024Characters()
    {
        var large = DiffTextWindow.Create(new('+', new string('x', 50_000)), 0, 50_000, 1, false);
        Assert.Equal(1024, large.Count);
    }

    [Fact]
    public void WhenTabsComeBeforeAndInsideTokens_ShouldWidenTheirRangesAndClipThemToTheWindow()
    {
        const string raw = "a\tbc\td ef";
        var window = new DiffTextWindow("", 6, 7);
        SyntaxToken[] tokens = [new(2, 4, SyntaxKind.String), new(7, 2, SyntaxKind.Keyword)];
        var visible = window.VisibleTokens(raw, tokens).ToArray();
        Assert.Equal([new SyntaxToken(0, 6, SyntaxKind.String)], visible);
    }

    [Fact]
    public void WhenARowShowsItsLineNumbers_ShouldKeepTheSplitSideAndAlignTheSparseUnifiedColumns()
    {
        var line = new PatchLine('+', "+added", null, 1_000_002);
        Assert.Equal("1000002", new DiffRowData(new(), 0, true).Numbers(line, true, 7));
        Assert.Equal("", new DiffRowData(new(), 0, true).Numbers(line, false, 7));
        Assert.Equal("        1000002", new DiffRowData(new(), 0, false).Numbers(line, true, 7));
    }

    // A wide character takes the columns it covers, so the window starts where the offset falls among them.
    [Fact]
    public void WhenWideCharactersComeBeforeTheOffset_ShouldStartTheWindowAtTheColumnsTheyCover()
    {
        var window = DiffTextWindow.Create(new('+', "+" + new string('中', 300)), 100, 50, 1, false);
        Assert.Equal(50, window.Start);
        Assert.Equal(99, window.StartColumn);
    }
}
