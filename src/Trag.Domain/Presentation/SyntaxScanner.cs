namespace Trag.Domain.Presentation;

// A bounded lexical scanner for one side of a diff, or for a file's own lines with no prefix column: the position in the
// line being scanned, the language, and whether a block comment is open, the one thing that carries from line to line.
// No grammars, regular expressions or reflection.
public sealed class SyntaxScanner(SyntaxLanguage language, int prefix = 1)
{
    private readonly List<SyntaxToken> tokens = [];
    // C-style code tells types, methods and members apart besides its keywords; XML and YAML have scanners of their own.
    private readonly CodeNames? names = language == SyntaxLanguage.CStyle ? new() : null;
    private readonly MarkupScanner? markup = language == SyntaxLanguage.Markup ? new() : null;
    private string text = "";
    private int position;
    private bool inBlockComment;

    public static SyntaxLanguage LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" or ".js" or ".cjs" or ".mjs" or ".jsx" or ".ts" or ".tsx" or ".c" or ".h" or ".cpp" or ".hpp" or ".java" or ".go" or ".rs" or
            ".css" => SyntaxLanguage.CStyle,
        ".py" or ".sh" or ".ps1" or ".toml" or ".rb" => SyntaxLanguage.Script,
        ".json" => SyntaxLanguage.Json,
        ".xml" or ".xaml" or ".axaml" or ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets" or ".config" or ".resx" or ".svg" or ".nuspec" or
            ".xsd" or ".xsl" or ".xslt" or ".slnx" or ".manifest" or ".html" or ".htm" => SyntaxLanguage.Markup,
        ".yml" or ".yaml" => SyntaxLanguage.Yaml,
        _ => SyntaxLanguage.Plain
    };

    // A hunk boundary: the lines in between are not shown, so a comment cannot continue across it.
    public void Reset()
    {
        inBlockComment = false;
        markup?.Reset();
    }

    // The tokens of one line. The prefix columns, a diff's +, - or space, are never part of a token.
    public SyntaxToken[] Scan(string line)
    {
        text = line;
        position = prefix;
        tokens.Clear();
        if (markup is not null) markup.Scan(line, prefix, tokens);
        else if (language == SyntaxLanguage.Yaml) YamlScanner.Scan(line, prefix, tokens);
        else Code();
        return tokens.ToArray();
    }

    private void Code()
    {
        names?.NewLine();
        while (position < text.Length)
        {
            var start = position;
            if (Next() is { } kind) tokens.Add(new(start, position - start, kind));
        }
    }

    private SyntaxKind? Next()
    {
        var c = text[position];
        if (inBlockComment || language == SyntaxLanguage.CStyle && Follows("/*")) return BlockComment();
        if (language == SyntaxLanguage.Script && c == '#' || language == SyntaxLanguage.CStyle && Follows("//")) return LineComment();
        if (c is '"' or '\'' || c == '`' && language == SyntaxLanguage.CStyle) return String(c);
        if (char.IsAsciiDigit(c)) return Number();
        if (char.IsLetter(c) || c == '_') return Word();
        names?.Passed(text, position);
        position++;
        return null;
    }

    private bool Follows(string opening) => text.AsSpan(position).StartsWith(opening, StringComparison.Ordinal);

    private SyntaxKind BlockComment()
    {
        if (!inBlockComment) position += 2;
        inBlockComment = true;
        while (position < text.Length)
        {
            if (text[position++] == '*' && Follows("/"))
            {
                position++;
                inBlockComment = false;
                break;
            }
        }
        return SyntaxKind.Comment;
    }

    private SyntaxKind LineComment()
    {
        position = text.Length;
        return SyntaxKind.Comment;
    }

    // A backslash escapes the next character; an unterminated string runs to the end of the line.
    private SyntaxKind String(char quote)
    {
        position++;
        while (position < text.Length)
        {
            if (text[position] == '\\')
            {
                position = Math.Min(text.Length, position + 2);
                continue;
            }
            if (text[position++] == quote) break;
        }
        return SyntaxKind.String;
    }

    private SyntaxKind Number()
    {
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '.' or '_')) position++;
        return SyntaxKind.Number;
    }

    private SyntaxKind? Word()
    {
        var start = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
        var keyword = IsKeyword(text.AsSpan(start, position - start));
        return names is null ? keyword ? SyntaxKind.Keyword : null : names.Of(text, start, position, keyword);
    }

    private static bool IsKeyword(ReadOnlySpan<char> word) => word is
        "public" or "private" or "protected" or "internal" or "static" or "class" or "record" or "struct" or "enum" or "interface" or
        "return" or "new" or "var" or "let" or "const" or "function" or "void" or "async" or "await" or "using" or "namespace" or
        "if" or "else" or "for" or "foreach" or "while" or "switch" or "case" or "default" or "break" or "continue" or
        "try" or "catch" or "finally" or "throw" or "true" or "false" or "null" or "undefined" or "this" or "base" or
        "import" or "from" or "export" or "def" or "in" or "is" or "not" or "and" or "or" or "None" or "True" or "False" or
        "int" or "string" or "bool" or "double" or "float" or "long" or "char" or "byte" or "fn" or "mut" or "pub" or "use" or "impl" or "match" or
        "package" or "func" or "type" or "extends" or "implements" or "override" or "readonly" or "sealed" or "partial" or "get" or "set" or
        "nameof" or "typeof" or "sizeof" or "out" or "ref" or "params" or "where" or "yield" or "lock" or "init" or "required" or "with" or
        "when" or "as" or "operator" or "event" or "delegate" or "object" or "decimal" or "short" or "uint" or "ulong" or "ushort" or "sbyte" or
        "virtual" or "abstract" or "extern" or "unsafe" or "do" or "goto" or "super" or "instanceof" or "trait" or "loop" or "self" or "lambda" or
        "pass" or "raise" or "elif" or "except" or "assert" or "del" or "global" or "defer" or "go" or "chan" or "range" or "map" or "mod" or "crate";
}
