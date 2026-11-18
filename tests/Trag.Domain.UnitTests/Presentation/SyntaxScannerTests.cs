using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

public sealed class SyntaxScannerTests
{
    // A line of the file itself, as blame shows it, has no diff prefix: its first column is code too.
    [Fact]
    public void WhenTheLinesHaveNoPrefix_ShouldScanFromTheFirstColumn()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle, prefix: 0);
        Assert.Equal([new(0, 5, SyntaxKind.Keyword), new(9, 3, SyntaxKind.String)], scanner.Scan("using x; \"a\""));
    }

    // A string that ends its line on an escaped quote is still open there, but strings do not run past a line: the next
    // line starts outside it.
    [Theory]
    [InlineData(SyntaxLanguage.CStyle, "+return x;", SyntaxKind.Keyword)]
    [InlineData(SyntaxLanguage.Json, "+true", SyntaxKind.Keyword)]
    public void WhenALineEndsInsideAStringOnAnEscapedQuote_ShouldStartTheNextLineOutsideIt(SyntaxLanguage language, string next, SyntaxKind kind)
    {
        var scanner = new SyntaxScanner(language);
        scanner.Scan("+\"open \\\"");

        var tokens = scanner.Scan(next);

        Assert.Equal(kind, Assert.Single(tokens, token => token.Start == 1).Kind);
    }

    [Fact]
    public void WhenALineOpensABlockComment_ShouldColorTheRestOfTheLineAsComment()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle);
        Assert.Equal([new(1, 4, SyntaxKind.Comment)], scanner.Scan("+/* a"));
    }

    [Fact]
    public void WhenTheNextLineClosesTheBlockComment_ShouldColorItAsCommentUpToTheClose()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle);
        Assert.Equal([new(1, 4, SyntaxKind.Comment)], scanner.Scan("+/* a"));
        Assert.Equal([new(1, 5, SyntaxKind.Comment)], scanner.Scan("+ b */ x"));
    }

    [Fact]
    public void WhenALineOpensACommentAfterOneClosed_ShouldColorTheNewCommentToo()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle);
        Assert.Equal([new(1, 4, SyntaxKind.Comment)], scanner.Scan("+/* a"));
        Assert.Equal([new(1, 5, SyntaxKind.Comment)], scanner.Scan("+ b */ x"));
        Assert.Equal([new(1, 4, SyntaxKind.Comment)], scanner.Scan("+/* c"));
    }

    // The builder resets the scanner where a hunk ends: the lines Git skipped may have closed the comment.
    [Fact]
    public void WhenAHunkEndsInsideABlockComment_ShouldStartTheNextLineOutsideIt()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle);
        Assert.Equal([new(1, 4, SyntaxKind.Comment)], scanner.Scan("+/* c"));
        scanner.Reset();
        Assert.Empty(scanner.Scan("+ b */ x"));
    }

    [Theory]
    [InlineData(SyntaxLanguage.CStyle, "+\"a\\\"b\" 'c'", 1, 6, SyntaxKind.String, 8, 3, SyntaxKind.String)]
    [InlineData(SyntaxLanguage.CStyle, "+x // y \"z\"", 3, 8, SyntaxKind.Comment, -1, 0, SyntaxKind.Comment)]
    [InlineData(SyntaxLanguage.Script, "+x # y", 3, 3, SyntaxKind.Comment, -1, 0, SyntaxKind.Comment)]
    [InlineData(SyntaxLanguage.CStyle, "+x # y", -1, 0, SyntaxKind.Comment, -1, 0, SyntaxKind.Comment)]
    [InlineData(SyntaxLanguage.CStyle, "+`t`", 1, 3, SyntaxKind.String, -1, 0, SyntaxKind.String)]
    [InlineData(SyntaxLanguage.Script, "+`t`", -1, 0, SyntaxKind.String, -1, 0, SyntaxKind.String)]
    [InlineData(SyntaxLanguage.Json, "+\"k\": true", 1, 3, SyntaxKind.String, 6, 4, SyntaxKind.Keyword)]
    [InlineData(SyntaxLanguage.CStyle, " 1_000.5f x2", 1, 8, SyntaxKind.Number, -1, 0, SyntaxKind.Number)]
    [InlineData(SyntaxLanguage.CStyle, "-return returned", 1, 6, SyntaxKind.Keyword, -1, 0, SyntaxKind.Keyword)]
    [InlineData(SyntaxLanguage.CStyle, "+\"open", 1, 5, SyntaxKind.String, -1, 0, SyntaxKind.String)]
    public void WhenALineIsScanned_ShouldGiveEachTokenItsExactRangeAndNeverColorTheDiffPrefix(SyntaxLanguage language, string line,
        int firstStart, int firstLength, SyntaxKind firstKind, int secondStart, int secondLength, SyntaxKind secondKind)
    {
        var expected = new List<SyntaxToken>();
        if (firstStart >= 0) expected.Add(new(firstStart, firstLength, firstKind));
        if (secondStart >= 0) expected.Add(new(secondStart, secondLength, secondKind));
        Assert.Equal(expected, new SyntaxScanner(language).Scan(line));
    }

    [Theory]
    [InlineData("Program.CS", SyntaxLanguage.CStyle)]
    [InlineData("tests/logo-style-test.cjs", SyntaxLanguage.CStyle)]
    [InlineData("tests/run-all.mjs", SyntaxLanguage.CStyle)]
    [InlineData("deploy.yml", SyntaxLanguage.Yaml)]
    [InlineData("tools.toml", SyntaxLanguage.Script)]
    [InlineData("data.json", SyntaxLanguage.Json)]
    [InlineData("notes.txt", SyntaxLanguage.Plain)]
    [InlineData("README", SyntaxLanguage.Plain)]
    public void WhenAFileIsShown_ShouldTakeItsLanguageFromItsExtension(string path, SyntaxLanguage expected) => Assert.Equal(expected, SyntaxScanner.LanguageOf(path));
}
