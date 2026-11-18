using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation;

public sealed class DiffPresentationBuilderTests
{
    [Fact]
    public async Task WhenAPatchHasTwoHunks_ShouldShowOneOmittedMarkerInsteadOfTheirRawHeaders()
    {
        var document = await TwoHunksAsync();
        Assert.Equal(7, document.Lines.Count);
        Assert.Equal("Unchanged lines omitted", document.Lines[5].Text);
    }

    // A copy read with its changed source is two file sections; nothing of either
    // file is left out between them.
    [Fact]
    public async Task WhenThePatchHoldsTwoFileSections_ShouldNotMarkOmittedLinesBetweenThem()
    {
        var document = await Build([new('h', "diff --git a/new.txt b/new.txt"), new('h', "new file mode 100644"), new('h', "--- /dev/null"), new('h', "+++ b/new.txt"),
            new('h', "@@ -0,0 +1,2 @@"), new('+', "+a", null, 1), new('+', "+b", null, 2),
            new('h', "diff --git a/old.txt b/old.txt"), new('h', "deleted file mode 100644"), new('h', "--- a/old.txt"), new('h', "+++ /dev/null"),
            new('h', "@@ -1,2 +0,0 @@"), new('-', "-a", 1), new('-', "-b", 2)]);

        Assert.Equal(["+a", "+b", "-a", "-b"], document.Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task WhenAPatchHasTwoHunks_ShouldAlignRemovedAndAddedLinesOnlyWithinTheirOwnHunk()
    {
        var document = await TwoHunksAsync();
        Assert.Equal([new(0, 0), new(1, 2), new(-1, 3), new(4, 4), new(5, 5), new DiffPair(6, -1)], document.SplitRows);
    }

    [Fact]
    public async Task WhenTheViewSwitchesBetweenUnifiedAndSplit_ShouldFindEachLineInTheRowThatHoldsIt()
    {
        var document = await TwoHunksAsync();
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var pair = document.SplitRows[document.ToSplit(i)];
            Assert.True(pair.Left == i || pair.Right == i);
        }
        Assert.Equal(6, document.ToUnified(5));
    }

    // The widest row is measured as the view shows it: a tab as an arrow and three spaces, and the marks of a CRLF ending
    // (while the diff's lines end both ways, as beside the LF line here) and of a missing newline at the end.
    [Theory]
    [InlineData("+a\tb", false, false)]
    [InlineData("+crlf", true, false)]
    [InlineData("+last", false, true)]
    public async Task WhenALineIsShownWithATabOrAMark_ShouldMeasureItAsItIsShown(string text, bool carriageReturn, bool noNewline)
    {
        var line = new PatchLine('+', text, null, 1) { HasCarriageReturn = carriageReturn, NoNewline = noNewline };
        var document = await Build([line, new PatchLine('+', "+x", null, 2)]);
        Assert.Equal(FileDiffText.Shown(line, document.ShowsLineEndings).Length, document.MaxColumns);
    }

    // The monospace font has no glyphs for them, and the fallback fonts draw an East Asian wide or fullwidth character
    // about 1.7 cells wide and an emoji past the basic plane about 2.3; a row gives them as many whole columns.
    [Theory]
    [InlineData("\u1100", 2)]
    [InlineData("\u4E2D", 2)]
    [InlineData("\uAC00", 2)]
    [InlineData("\uF900", 2)]
    [InlineData("\uFE30", 2)]
    [InlineData("\uFF21", 2)]
    [InlineData("\uFFE5", 2)]
    [InlineData("\U0001F600", 3)]
    public async Task WhenALineHoldsCharactersDrawnWiderThanACell_ShouldGiveEachTheColumnsItCovers(string character, int columns)
    {
        var document = await Build([new PatchLine('+', "+" + string.Concat(Enumerable.Repeat(character, 10)), null, 1)]);
        Assert.Equal(1 + 10 * columns, document.MaxColumns);
    }

    [Fact]
    public async Task WhenTheLastLineHasNoNewline_ShouldKeepThatMark()
    {
        var document = await TwoHunksAsync();
        Assert.True(document.Lines[6].NoNewline);
    }

    [Fact]
    public async Task WhenARemovedLineOpensABlockComment_ShouldColorTheLinesAfterItAsCommentOnlyOnTheRemovedSide()
    {
        var result = await OpenCommentAsync();
        Assert.Equal(SyntaxKind.Comment, Assert.Single(result.OldTokens[2]).Kind);
        Assert.Equal(SyntaxKind.Keyword, Assert.Single(result.NewTokens[2]).Kind);
        Assert.Contains(result.NewTokens[1], token => token.Kind == SyntaxKind.Number);
    }

    [Fact]
    public async Task WhenAHunkEndsInsideABlockComment_ShouldStartTheNextHunkOutsideIt()
    {
        var result = await OpenCommentAsync();
        Assert.Equal(SyntaxKind.Keyword, Assert.Single(result.OldTokens[4]).Kind);
    }

    // The colors come from Trag's own scanner, not from grammars loaded through reflection.
    [Theory]
    [InlineData("code.cs", "+public string value = \"hello\"; // sample")]
    [InlineData("script.py", "+return \"hello\" # sample")]
    [InlineData("data.json", "+\"value\": true")]
    public async Task WhenTheFileIsInACommonLanguage_ShouldColorItsStringsAndKeywords(string path, string text)
    {
        var result = await BuildAsync(new([], [new('+', text)], DiffContentKind.Text, ""), path, default);
        Assert.Contains(result.NewTokens[0], token => token.Kind == SyntaxKind.String);
        Assert.Contains(result.NewTokens[0], token => token.Kind == SyntaxKind.Keyword);
    }

    [Fact]
    public async Task WhenADiffIsLargerThanTheHighlightBudget_ShouldColorOnlyUpToTheBudgetAndSaySo()
    {
        var lines = LargeLines();
        var result = await Build(lines);
        Assert.Contains("limit", result.SyntaxNotice);
        Assert.InRange(result.OldTokens.Sum(t => t.Length) + result.NewTokens.Sum(t => t.Length), 1, SyntaxBudget.TokenLimit);
        Assert.Equal(50_000, result.SplitRows.Count);
        Assert.Empty(result.NewTokens[^1]);
    }

    // One line longer than the highlight budget stops the coloring there: it and every line after it stay plain.
    [Fact]
    public async Task WhenOneLineIsFortyThousandCharacters_ShouldStopColoringAtThatLine()
    {
        var result = await Build([new('+', "+" + new string('a', 40_000), null, 1), new('+', "+return x;", null, 2)]);

        Assert.Contains("limit", result.SyntaxNotice);
        Assert.All(result.NewTokens, tokens => Assert.Empty(tokens));
    }

    // The budget counts both sides of a line against each limit: a first line of half the token limit is colored, one
    // character more is not.
    [Theory]
    [InlineData(SyntaxBudget.TokenLimit / 2, true)]
    [InlineData(SyntaxBudget.TokenLimit / 2 + 1, false)]
    public async Task WhenALineReachesTheTokenBudgetExactly_ShouldStillColorIt(int length, bool colored)
    {
        var result = await Build([new('+', "+//" + new string('a', length - 3), null, 1)]);

        Assert.Equal((colored ? FileDiffText.SyntaxColors : FileDiffText.SyntaxLimitReached, colored), (result.SyntaxNotice, result.NewTokens[0].Length != 0));
    }

    // Fifty lines of 10,000 characters scan exactly the million characters the budget allows; the next line stays plain.
    [Fact]
    public async Task WhenTheLinesReachTheCharacterBudgetExactly_ShouldColorThemAndLeaveTheNextPlain()
    {
        var lines = Enumerable.Range(1, 51).Select(number => new PatchLine('+', "+//" + new string('a', SyntaxBudget.CharacterLimit / 100 - 3), null, number)).ToArray();

        var result = await Build(lines);

        Assert.Equal(FileDiffText.SyntaxLimitReached, result.SyntaxNotice);
        Assert.Equal(50, result.NewTokens.Count(tokens => tokens.Length != 0));
        Assert.Empty(result.NewTokens[50]);
    }

    [Fact]
    public async Task WhenTheExtensionIsUnknown_ShouldShowPlainText()
    {
        var lines = LargeLines();
        var plain = await BuildAsync(new([], lines, DiffContentKind.Text, ""), "file.custom", default);
        Assert.Equal("Plain text", plain.SyntaxNotice); Assert.All(plain.NewTokens, Assert.Empty);
    }

    [Fact]
    public async Task WhenTheBuildIsCanceled_ShouldStopWithACancellation()
    {
        var lines = LargeLines();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildAsync(new([], lines, DiffContentKind.Text, ""), "a.cs", canceled.Token));
    }

    [Theory]
    [InlineData("A", "New File", true, false)]
    [InlineData("?", "New File", true, false)]
    [InlineData("D", "Deleted File", false, true)]
    [InlineData("R100", "Renamed", false, false)]
    [InlineData("C", "Copied", false, false)]
    [InlineData("U", "Conflict", false, false)]
    [InlineData("M", "Modified", false, false)]
    public async Task WhenAFileHasAStatusLetter_ShouldTakeTheBadgeWordAndTheNewAndDeletedFlagsFromIt(string status, string label, bool added, bool removed)
    {
        var metadata = new FileDiffMetadata(new("sample.cs"u8.ToArray()), status, "100644", "100644", "", "");
        var presentation = await BuildAsync(new([metadata], [], DiffContentKind.Text, ""), "sample.cs", default);

        Assert.Equal(label, presentation.ChangeLabel);
        Assert.Equal(added, presentation.IsNewFile);
        Assert.Equal(removed, presentation.IsDeletedFile);
    }

    // A staged rename's metadata is read without rename detection, as the deletion of the old path and the addition of the
    // new one, so both of a rename's paths arrive with a status of their own. The status list calls the row Renamed; the badge
    // above its diff has to say the same, not Modified.
    [Fact]
    public async Task WhenARenameArrivesAsTheDeletionOfItsOldPathAndTheAdditionOfItsNewOne_ShouldCallItRenamed()
    {
        var added = new FileDiffMetadata(new("new.cs"u8.ToArray()), "A", "000000", "100644", "", "");
        var deleted = new FileDiffMetadata(new("old.cs"u8.ToArray()), "D", "100644", "000000", "", "");

        var presentation = await BuildAsync(new([added, deleted], [], DiffContentKind.Text, ""), "new.cs", default);

        Assert.Equal("Renamed", presentation.ChangeLabel);
    }

    [Fact]
    public async Task WhenAFileHasNoMetadataAndNoLines_ShouldShowNoBadgeAtAll()
    {
        var presentation = await Build([]);

        Assert.Equal("", presentation.ChangeLabel);
        Assert.False(presentation.IsNewFile);
        Assert.False(presentation.IsDeletedFile);
    }

    // Two hunks: a change block of one removed and two added lines between context lines, then a removed line without a
    // newline at the end.
    // A file that ends every line the same way needs no mark on each of them; a last line without any ending is neither.
    [Fact]
    public async Task WhenEveryLineEndsWithCrlf_ShouldNotMarkTheLineEndings()
    {
        var document = await Build([new('h', "@@"), new('+', "+one", null, 1) { HasCarriageReturn = true },
            new('+', "+two", null, 2) { HasCarriageReturn = true }, new('+', "+three", null, 3) { NoNewline = true }]);
        Assert.False(document.ShowsLineEndings);
    }

    // A line whose only change is its ending looks the same on both sides; the marks tell the two apart.
    [Fact]
    public async Task WhenLinesEndBothWithCrlfAndWithLf_ShouldMarkTheLineEndings()
    {
        var document = await Build([new('h', "@@"), new('-', "-same", 1), new('+', "+same", null, 1) { HasCarriageReturn = true }]);
        Assert.True(document.ShowsLineEndings);
    }

    private static Task<DiffPresentation> TwoHunksAsync()
    {
        PatchLine[] lines = [new('h', "@@ -10,4 +20,5 @@"), new(' ', " same", 10, 20),
            new('-', "-old", 11), new('+', "+new", null, 21), new('+', "+extra", null, 22),
            new(' ', " end", 12, 23), new('h', "@@ -40 +50 @@"), new('-', "-gone", 40) { NoNewline = true }];
        return Build(lines);
    }

    // A removed line opens a block comment the added line beside it does not have; the hunk ends with the comment open.
    private static Task<DiffPresentation> OpenCommentAsync() =>
        Build([new('h', "@@"), new('-', "-/* old", 1), new('+', "+var value = 42;", null, 1),
            new(' ', " return value;", 2, 2), new('h', "@@"), new(' ', " return value;", 90, 90)]);

    private static PatchLine[] LargeLines() =>
        Enumerable.Range(0, 50_000).Select(i => new PatchLine('+', "+var x = 123; // text", null, i + 1)).ToArray();

    private static Task<DiffPresentation> Build(PatchLine[] lines) => BuildAsync(new([], lines, DiffContentKind.Text, ""), "sample.cs", default);

    private static Task<DiffPresentation> BuildAsync(FileDiff diff, string path, CancellationToken ct) =>
        Task.FromResult(DiffPresentationBuilder.Build(diff, path, ct));
}
