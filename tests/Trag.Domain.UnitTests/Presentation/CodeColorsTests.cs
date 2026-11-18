using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

// Code colored the way an IDE colors it, from the text alone: besides the keywords, strings, comments and numbers, the
// types, the methods and the members. With no compiler behind it the kind of a name is read from where it stands: what
// a declaration or new names, what a call is made on, what follows a dot, what a capital and its neighbours suggest. A
// namespace stays plain.
public sealed class CodeColorsTests
{
    private const SyntaxKind Keyword = SyntaxKind.Keyword;
    private const SyntaxKind Type = SyntaxKind.Type;
    private const SyntaxKind Method = SyntaxKind.Method;
    private const SyntaxKind Member = SyntaxKind.Member;

    private static (string Text, SyntaxKind Kind)[] Colors(params string[] lines)
    {
        var scanner = new SyntaxScanner(SyntaxLanguage.CStyle, prefix: 0);
        return [.. lines.SelectMany(line => scanner.Scan(line).Select(token => (line.Substring(token.Start, token.Length), token.Kind)))];
    }

    [Fact]
    public void WhenARecordIsDeclared_ShouldColorItsNameAndTheTypesOfItsParameters() =>
        Assert.Equal([("public", Keyword), ("sealed", Keyword), ("record", Keyword), ("ConnectionOptions", Type), ("string", Keyword), ("ClientId", Member),
                ("AccountKind", Type), ("AccountKind", Member), ("bool", Keyword), ("UseTeamsWebSignIn", Member), ("false", Keyword)],
            Colors("public sealed record ConnectionOptions(string ClientId, AccountKind AccountKind, bool UseTeamsWebSignIn = false)"));

    // A lowercase local stays plain; a type is what a member or a method is reached through.
    [Fact]
    public void WhenMethodsAndMembersAreReached_ShouldColorThemApartFromTheTypesTheyBelongTo() =>
        Assert.Equal([("Guid", Type), ("TryParse", Method), ("ClientId", Member), ("out", Keyword), ("var", Keyword), ("Guid", Type), ("Empty", Member),
                ("Enum", Type), ("IsDefined", Method), ("AccountKind", Member)],
            Colors(": Guid.TryParse(ClientId, out var id) && id != Guid.Empty && Enum.IsDefined(AccountKind);"));

    // A using that names a namespace leaves it plain; one that declares a variable is a statement like any other.
    [Fact]
    public void WhenANamespaceIsNamed_ShouldLeaveItPlain() =>
        Assert.Equal([("using", Keyword), ("namespace", Keyword), ("using", Keyword), ("var", Keyword), ("File", Type), ("OpenRead", Method)],
            Colors("using Trag.Domain.Blame;", "namespace Trag.ViewModels.History;", "using var stream = File.OpenRead(path);"));

    [Fact]
    public void WhenGenericNullableAndAttributeTypesAreWritten_ShouldColorThemAsTypes() =>
        Assert.Equal([("ObservableProperty", Type), ("public", Keyword), ("partial", Keyword), ("IReadOnlyList", Type), ("BlameRow", Type), ("Rows", Member),
                ("get", Keyword), ("private", Keyword), ("set", Keyword), ("public", Keyword), ("Dictionary", Type), ("string", Keyword), ("BlameRow", Type),
                ("Selected", Member), ("BlameRow", Type), ("Current", Member), ("Dictionary", Type), ("BlameRow", Type), ("BlameLine", Type)],
            Colors("[ObservableProperty] public partial IReadOnlyList<BlameRow> Rows { get; private set; } = [];",
                "public Dictionary<string, BlameRow> Selected; BlameRow? Current;", "Dictionary<BlameRow, BlameLine> lines"));

    // What new makes is a type, though a call follows it.
    [Fact]
    public void WhenAnObjectIsMade_ShouldColorWhatItMakesAsAType() =>
        Assert.Equal([("var", Keyword), ("new", Keyword), ("List", Type), ("BlameRow", Type), ("Add", Method), ("new", Keyword), ("BlameLine", Type),
                ("1", SyntaxKind.Number), ("nameof", Keyword), ("Path", Member)],
            Colors("var rows = new List<BlameRow>(); rows.Add(new BlameLine(1)); nameof(Path)"));

    // What follows a dot is a member, lowercase or not; the local of the same name stays plain.
    [Fact]
    public void WhenAMemberIsReachedThroughADot_ShouldColorItAsAMember() =>
        Assert.Equal([("this", Keyword), ("blame", Member)], Colors("this.blame = blame;"));

    // A ternary is no nullable type: with a space before the question mark the name before it is a member.
    [Fact]
    public void WhenAConditionChoosesBetweenMembers_ShouldColorThemAsMembers() =>
        Assert.Equal([("UseTeamsWebSignIn", Member), ("AccountKind", Member), ("AccountKind", Type), ("Personal", Member)],
            Colors("UseTeamsWebSignIn ? AccountKind == AccountKind.Personal"));
}
