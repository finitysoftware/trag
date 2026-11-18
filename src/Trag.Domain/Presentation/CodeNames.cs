namespace Trag.Domain.Presentation;

// The kind of a name in C-style code, read from where it stands on its line, since there is no compiler to ask. What a
// declaration or new names is a type, and so is a capitalized name inside a generic's brackets, after an attribute's
// bracket or a colon, or before another name, a dot, a generic's bracket or a nullable mark; a name a call is made on is
// a method; a name after a dot, and any other capitalized name, is a member. A lowercase name is a local and stays plain,
// and so does the namespace a using or namespace names. The scanner hands every word and every character it passes.
internal sealed class CodeNames
{
    private string previousWord = "";
    private bool namespaceNext;
    private bool inNamespace;
    private int genericDepth;

    public void NewLine()
    {
        previousWord = "";
        namespaceNext = inNamespace = false;
        genericDepth = 0;
    }

    // A bracket right after a name opens a generic, its partner closes it.
    public void Passed(string text, int at)
    {
        var c = text[at];
        if (c == '<' && at > 0 && IsNamePart(text[at - 1])) genericDepth++;
        else if (c == '>' && genericDepth > 0) genericDepth--;
    }

    public SyntaxKind? Of(string text, int start, int end, bool keyword)
    {
        var before = previousWord;
        previousWord = text[start..end];
        if (keyword)
        {
            namespaceNext = previousWord is "using" or "namespace" or "package" or "import";
            return SyntaxKind.Keyword;
        }
        inNamespace |= namespaceNext;
        namespaceNext = false;
        if (inNamespace) return null;
        if (before is "class" or "record" or "struct" or "interface" or "enum" or "new" or "extends" or "implements") return SyntaxKind.Type;
        var (previous, next) = (Previous(text, start), Next(text, end));
        if (next == '(') return SyntaxKind.Method;
        if (previous == '.') return SyntaxKind.Member;
        if (!char.IsUpper(text[start])) return null;
        var typed = genericDepth > 0 || previous is '[' or ':' || next is '.' or '<' or '>' || char.IsLetter(next) || next == '_'
            || end < text.Length && text[end] == '?';
        return typed ? SyntaxKind.Type : SyntaxKind.Member;
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static char Previous(string text, int start)
    {
        var at = start - 1;
        while (at >= 0 && char.IsWhiteSpace(text[at])) at--;
        return at >= 0 ? text[at] : '\0';
    }

    private static char Next(string text, int end)
    {
        var at = end;
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        return at < text.Length ? text[at] : '\0';
    }
}
