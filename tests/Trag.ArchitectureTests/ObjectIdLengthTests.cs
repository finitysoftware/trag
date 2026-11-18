using System.Text.RegularExpressions;

namespace Trag.ArchitectureTests;

// Git names an object with 40 hex characters in a SHA-1 repository and with 64 in a SHA-256 one, so code that takes an
// ID for 40 characters reads a SHA-256 repository wrong. In code the number 40 stands only as "40 or 64", and no string
// literal holds a quantifier ending at 40 or a 40-character lowercase hex ID.
public sealed partial class ObjectIdLengthTests
{
    [Fact]
    public void ScannerFindsEveryFortyCharacterAssumptionAndIgnoresTheSha256PairCommentsAndOtherNumbers()
    {
        const string text = """
            // An object ID has 40 hex characters.
            var valid = value.Length is 40 or 64;
            if (value.Length is not (40 or 64)) return false;
            var sizes = new[] { 140, 400, 1.40 };
            var refused = "HTTP 403";
            const string EmptySha256Tree = "6ef19b41225c5369f1c104d45d8d85efa9b057b53b14b4b9b939dd74decc5321";
            if (id.Length == 40) return id;
            var head = id[..40];
            var same = id.Substring(0, 40);
            var zero = new string('0', 40);
            [GeneratedRegex("^[0-9a-f]{40}$")]
            [GeneratedRegex("^[0-9a-f]{7,40}$")]
            const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
            """;
        Assert.Equal(
            [
                "if (id.Length == 40) return id;",
                "var head = id[..40];",
                "var same = id.Substring(0, 40);",
                "var zero = new string('0', 40);",
                "[GeneratedRegex(\"^[0-9a-f]{40}$\")]",
                "[GeneratedRegex(\"^[0-9a-f]{7,40}$\")]",
                "const string EmptyTree = \"4b825dc642cb6eb9a060e54bf8d69288fbee4904\";",
            ],
            Assumptions(text));
    }

    [Fact]
    public void NoProductionSourceAssumesAFortyCharacterObjectId() =>
        Assert.Empty(RepositoryRoot.AllProductionSources().SelectMany(file => Assumptions(file.Text).Select(line => file.Display + ": " + line)));

    // The lines of a source that take an ID for 40 characters, those found in its code before those in its literals.
    private static IReadOnlyList<string> Assumptions(string text)
    {
        var inCode = FortyInCode().Matches(CSharpSource.Strip(text)).Select(match => match.Index);
        var inLiterals = CSharpSource.StringLiteralsAt(text)
            .SelectMany(literal => FortyInLiteral().Matches(literal.Text).Select(match => literal.Start + match.Index));
        return inCode.Concat(inLiterals).Select(index => LineAt(text, index)).ToArray();
    }

    // Strip keeps every character's place, so an index into the stripped code is one into the source.
    private static string LineAt(string text, int index)
    {
        var start = text.LastIndexOf('\n', index) + 1;
        var end = text.IndexOf('\n', index);
        return text[start..(end < 0 ? text.Length : end)].Trim();
    }

    // The number 40 on its own, not inside 140, 400 or 1.40, unless " or 64" follows it.
    [GeneratedRegex(@"(?<!\w|\d\.)40(?!\w| or 64)")]
    private static partial Regex FortyInCode();

    // A quantifier that ends at 40, or a run of exactly 40 hex characters.
    [GeneratedRegex(@"\{(?:\d+,)?40\}|(?<![0-9a-f])[0-9a-f]{40}(?![0-9a-f])")]
    private static partial Regex FortyInLiteral();
}
