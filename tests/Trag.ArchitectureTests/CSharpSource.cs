using System.Text;
using System.Text.RegularExpressions;

namespace Trag.ArchitectureTests;

internal sealed record TypeDeclaration(string Name, string Kind, bool IsPartial, int Line);

// A deliberately small scanner: comments and string literals are blanked (line breaks kept), then braces give the nesting depth
// and a regex finds type declarations at the top level. It is not a parser; it only has to be right for this repository's style.
internal static partial class CSharpSource
{
    private static readonly HashSet<string> NotTypeNames = new(StringComparer.Ordinal) { "where", "new", "notnull", "unmanaged", "default", "struct", "class" };

    public static IReadOnlyList<TypeDeclaration> TopLevelTypes(string text)
    {
        var stripped = Strip(text);
        var result = new List<TypeDeclaration>();
        var depth = 0;
        var namespaceDepth = 0; // 1 while inside a block-scoped namespace, 0 for file-scoped namespaces
        var pendingNamespace = false;
        var line = 1;
        for (var i = 0; i < stripped.Length; i++)
        {
            var c = stripped[i];
            if (c == '\n') { line++; continue; }
            if (c == '{')
            {
                if (pendingNamespace) { namespaceDepth = depth + 1; pendingNamespace = false; }
                depth++;
                continue;
            }
            if (c == '}') { depth--; if (depth < namespaceDepth) namespaceDepth = 0; continue; }
            if (!char.IsLetter(c) || (i > 0 && (char.IsLetterOrDigit(stripped[i - 1]) || stripped[i - 1] == '_' || stripped[i - 1] == '.'))) continue;
            var match = Declaration().Match(stripped, i);
            if (!match.Success || match.Index != i) { if (StartsWithWord(stripped, i, "namespace")) pendingNamespace = !LineEndsWithSemicolon(stripped, i); continue; }
            var name = match.Groups["name"].Value;
            if (NotTypeNames.Contains(name)) continue;
            if (depth == namespaceDepth)
                result.Add(new(name, match.Groups["kind"].Value, match.Groups["modifiers"].Value.Contains("partial", StringComparison.Ordinal), line));
            i += match.Length - 1;
            line += match.Value.Count(ch => ch == '\n');
        }
        return result;
    }

    private static bool StartsWithWord(string text, int index, string word) =>
        string.CompareOrdinal(text, index, word, 0, word.Length) == 0 && (index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]));

    private static bool LineEndsWithSemicolon(string text, int index)
    {
        var end = text.IndexOf('\n', index);
        var segment = end < 0 ? text[index..] : text[index..end];
        return segment.TrimEnd().EndsWith(';');
    }

    // The string literals of a source as written, prefixes and quotes included; none in a comment.
    public static IReadOnlyList<string> StringLiterals(string text) => StringLiteralsAt(text).Select(literal => literal.Text).ToArray();

    // The same literals with where each starts in the source, for a rule that reads what the literal is written into.
    public static IReadOnlyList<(string Text, int Start)> StringLiteralsAt(string text)
    {
        var literals = new List<(string, int)>();
        Strip(text, (literal, start) => literals.Add((literal, start)));
        return literals;
    }

    // Replaces comments, string and character literals with spaces so that braces and keywords inside them are ignored.
    public static string Strip(string text) => Strip(text, null);

    // Each string literal blanked is also handed to literal, with where it starts.
    private static string Strip(string text, Action<string, int>? literal)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') { builder.Append(' '); i++; }
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                AppendBlank(builder, text, i, end); i = end;
                continue;
            }
            if (c == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                var quotes = 3; while (i + quotes < text.Length && text[i + quotes] == '"') quotes++;
                var closing = new string('"', quotes);
                var end = text.IndexOf(closing, i + quotes, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + quotes;
                literal?.Invoke(text[i..end], i);
                AppendBlank(builder, text, i, end); i = end;
                continue;
            }
            var verbatim = c == '@' && i + 1 < text.Length && text[i + 1] == '"';
            var interpolatedVerbatim = (c == '$' && i + 2 < text.Length && text[i + 1] == '@' && text[i + 2] == '"') || (c == '@' && i + 2 < text.Length && text[i + 1] == '$' && text[i + 2] == '"');
            var interpolated = c == '$' && i + 1 < text.Length && text[i + 1] == '"';
            if (verbatim || interpolatedVerbatim || interpolated || c == '"')
            {
                var start = i;
                i += interpolatedVerbatim ? 3 : verbatim || interpolated ? 2 : 1;
                var literalVerbatim = verbatim || interpolatedVerbatim;
                while (i < text.Length)
                {
                    if (literalVerbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                    if (!literalVerbatim && text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') { i++; break; }
                    i++;
                }
                literal?.Invoke(text[start..Math.Min(i, text.Length)], start);
                AppendBlank(builder, text, start, Math.Min(i, text.Length));
                continue;
            }
            if (c == '\'')
            {
                var start = i; i++;
                while (i < text.Length && text[i] != '\'' && text[i] != '\n') { if (text[i] == '\\') i++; i++; }
                if (i < text.Length && text[i] == '\'') i++;
                AppendBlank(builder, text, start, Math.Min(i, text.Length));
                continue;
            }
            builder.Append(c); i++;
        }
        return builder.ToString();
    }

    private static void AppendBlank(StringBuilder builder, string text, int start, int end)
    {
        for (var i = start; i < end; i++) builder.Append(text[i] == '\n' ? '\n' : ' ');
    }

    [GeneratedRegex(@"(?<modifiers>(?:(?:public|internal|private|protected|sealed|static|abstract|readonly|partial|unsafe|file|new)\s+)*)(?<kind>class|record\s+struct|record\s+class|record|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();
}
