using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

// XML and its dialects, XAML and AXAML and the project files among them, colored as an IDE colors them: the tags, the
// attribute names, their values and the markup extension inside a value, and the comments, with a tag and a comment
// followed across the lines they span. Text between the tags stays plain.
public sealed class MarkupColorsTests
{
    private const SyntaxKind Tag = SyntaxKind.Tag;
    private const SyntaxKind Name = SyntaxKind.Member;
    private const SyntaxKind Value = SyntaxKind.String;

    private static (string Text, SyntaxKind Kind)[] Colors(params string[] lines)
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.Markup, prefix: 0);
        return [.. lines.SelectMany(line => scanner.Scan(line).Select(token => (line.Substring(token.Start, token.Length), token.Kind)))];
    }

    [Fact]
    public void WhenAnElementHasAttributes_ShouldColorItsTagTheirNamesAndTheirValues() =>
        Assert.Equal([("<Button", Tag), ("Grid.Column", Name), ("\"2\"", Value), ("Name", Name), ("\"CloseBlameButton\"", Value), ("/>", Tag)],
            Colors("<Button Grid.Column=\"2\" Name=\"CloseBlameButton\" />"));

    // The extension's name is a type, as in code, and what it reaches are members.
    [Fact]
    public void WhenAValueIsAMarkupExtension_ShouldColorItsNameAndWhatItReaches() =>
        Assert.Equal([("<TextBlock", Tag), ("Text", Name), ("\"", Value), ("Binding", SyntaxKind.Type), ("Path", Name), ("Converter", Name),
                ("x:Static", SyntaxKind.Type), ("Converters.Upper", Name), ("\"", Value), (">", Tag)],
            Colors("<TextBlock Text=\"{Binding Path, Converter={x:Static Converters.Upper}}\">"));

    [Fact]
    public void WhenATagSpansLines_ShouldColorTheAttributesOnTheLinesAfterIt() =>
        Assert.Equal([("<UserControl", Tag), ("xmlns", Name), ("\"https://github.com/avaloniaui\"", Value), ("x:Class", Name),
                ("\"Trag.Desktop.Views.BlameView\"", Value), (">", Tag), ("<Grid", Tag), ("/>", Tag)],
            Colors("<UserControl xmlns=\"https://github.com/avaloniaui\"", "             x:Class=\"Trag.Desktop.Views.BlameView\">", "  <Grid />"));

    [Fact]
    public void WhenACommentSpansLines_ShouldColorItUntilItCloses() =>
        Assert.Equal([("<!-- A blamed file", SyntaxKind.Comment), ("     line by line. -->", SyntaxKind.Comment), ("<Grid", Tag), (">", Tag), ("</Grid", Tag), (">", Tag)],
            Colors("<!-- A blamed file", "     line by line. --> <Grid>", "</Grid>"));

    [Fact]
    public void WhenTextStandsBetweenTags_ShouldLeaveItPlain() =>
        Assert.Equal([("<Run", Tag), (">", Tag), ("</Run", Tag), (">", Tag)], Colors("<Run>Hello &amp; welcome</Run>"));

    [Fact]
    public void WhenADeclarationOpensTheFile_ShouldColorItAsATag() =>
        Assert.Equal([("<?xml", Tag), ("version", Name), ("\"1.0\"", Value), ("encoding", Name), ("\"utf-8\"", Value), ("?>", Tag)],
            Colors("<?xml version=\"1.0\" encoding=\"utf-8\"?>"));

    // A diff resets the scanner where a hunk ends: the lines Git skipped may have closed the comment or the tag.
    [Fact]
    public void WhenTheScannerIsReset_ShouldStartOutsideACommentAndATag()
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.Markup, prefix: 0);
        scanner.Scan("<!-- open");
        scanner.Reset();
        Assert.Empty(scanner.Scan("text --> more"));
        scanner.Scan("<Grid Name=\"a\"");
        scanner.Reset();
        Assert.Empty(scanner.Scan("Width=\"2\""));
    }

    [Theory]
    [InlineData("Views/MainWindow.axaml")]
    [InlineData("App.xaml")]
    [InlineData("src/Trag.Desktop/Trag.Desktop.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("app.config")]
    [InlineData("data.xml")]
    public void WhenAFileIsXmlOrADialectOfIt_ShouldTakeMarkupAsItsLanguage(string path) => Assert.Equal(SyntaxLanguage.Markup, SyntaxScanner.LanguageOf(path));
}
