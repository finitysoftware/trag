using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

// YAML colored as an IDE colors it: the keys, their values, the words YAML gives a meaning, numbers and comments, in a
// list item too. A hash starts a comment only at the start of a line or after a space, so one inside a value or a quoted
// string is the value's.
public sealed class YamlColorsTests
{
    private const SyntaxKind Key = SyntaxKind.Member;
    private const SyntaxKind Value = SyntaxKind.String;

    private static (string Text, SyntaxKind Kind)[] Colors(params string[] lines)
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.Yaml, prefix: 0);
        return [.. lines.SelectMany(line => scanner.Scan(line).Select(token => (line.Substring(token.Start, token.Length), token.Kind)))];
    }

    [Fact]
    public void WhenAKeyHasAValueAndAComment_ShouldColorAllThree() =>
        Assert.Equal([("name", Key), ("build", Value), ("# runs on every push", SyntaxKind.Comment)], Colors("name: build # runs on every push"));

    [Fact]
    public void WhenAValueIsAWordYamlKnowsOrANumber_ShouldColorItSo() =>
        Assert.Equal([("enabled", Key), ("true", SyntaxKind.Keyword), ("retries", Key), ("3", SyntaxKind.Number), ("missing", Key), ("null", SyntaxKind.Keyword)],
            Colors("enabled: true", "retries: 3", "missing: null"));

    [Fact]
    public void WhenAListItemHoldsAKeyOrAValue_ShouldColorThem() =>
        Assert.Equal([("uses", Key), ("actions/checkout@v4", Value), ("dotnet test", Value), ("http://host/x", Value)],
            Colors("    - uses: actions/checkout@v4", "    - dotnet test", "    - http://host/x"));

    [Fact]
    public void WhenAHashIsInsideAValueOrAQuotedString_ShouldKeepItInTheValue() =>
        Assert.Equal([("url", Key), ("http://host/page#top", Value), ("run", Key), ("\"dotnet test # all\"", Value)],
            Colors("url: http://host/page#top", "  run: \"dotnet test # all\""));

    // Whitespace that is not a space, after a key or alone on a line, is no value to color.
    [Fact]
    public void WhenOnlyATabFollows_ShouldColorNoValue() =>
        Assert.Equal([("key", Key)], Colors("key: \t", "\t", "  \t  "));

    [Fact]
    public void WhenALineIsACommentOrAKeyAlone_ShouldColorIt() =>
        Assert.Equal([("# deploy", SyntaxKind.Comment), ("on", Key)], Colors("# deploy", "on:"));
}
