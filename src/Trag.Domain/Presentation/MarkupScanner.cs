namespace Trag.Domain.Presentation;

// XML and its dialects line by line: a tag from its bracket through its name, the attribute names inside it, their
// quoted values and the tag's closing bracket; a value that opens a markup extension colors the extension's name as a
// type and what it reaches as members. A comment and a tag can span lines, so whether one is open carries from line
// to line. Text between the tags stays plain.
internal sealed class MarkupScanner
{
    private string text = "";
    private int position;
    private List<SyntaxToken> tokens = [];
    private bool inComment;
    private bool inTag;

    public void Reset() => inComment = inTag = false;

    public void Scan(string line, int start, List<SyntaxToken> found)
    {
        (text, position, tokens) = (line, start, found);
        while (position < text.Length)
        {
            if (inComment) Comment();
            else if (inTag) InTag();
            else if (Follows("<!--")) inComment = true;
            else if (text[position] == '<' && position + 1 < text.Length && (IsNameStart(text[position + 1]) || text[position + 1] is '/' or '?')) Open();
            else position++;
        }
    }

    private void Comment()
    {
        var close = text.IndexOf("-->", position, StringComparison.Ordinal);
        var end = close < 0 ? text.Length : close + 3;
        Add(position, end, SyntaxKind.Comment);
        inComment = close < 0;
    }

    // The bracket, a slash or question mark after it, and the name.
    private void Open()
    {
        var start = position;
        position += text[position + 1] is '/' or '?' ? 2 : 1;
        Add(start, Name(), SyntaxKind.Tag);
        inTag = true;
    }

    private void InTag()
    {
        var c = text[position];
        if (c is '/' or '?' && Follows(c + ">")) Close(2);
        else if (c == '>') Close(1);
        else if (c is '"' or '\'') Value(c);
        else if (IsNameStart(c)) Add(position, Name(), SyntaxKind.Member);
        else position++;
    }

    private void Close(int length)
    {
        Add(position, position + length, SyntaxKind.Tag);
        inTag = false;
    }

    // A quoted value, to its closing quote or the end of the line.
    private void Value(char quote)
    {
        var close = text.IndexOf(quote, position + 1);
        var end = close < 0 ? text.Length : close + 1;
        if (position + 1 < text.Length && text[position + 1] == '{') Extension(end, close >= 0);
        else Add(position, end, SyntaxKind.String);
    }

    // "{Binding Path, Converter={x:Static Converters.Upper}}": the quotes are the value's, a name right after a brace
    // is the extension's, every other name is a member it reaches.
    private void Extension(int end, bool closed)
    {
        Add(position, position + 1, SyntaxKind.String);
        var inner = closed ? end - 1 : end;
        var afterBrace = false;
        while (position < inner)
        {
            var c = text[position];
            if (IsNameStart(c) || c == '$')
            {
                var start = position;
                while (position < inner && (IsNamePart(text[position]) || text[position] == '$')) position++;
                Add(start, position, afterBrace ? SyntaxKind.Type : SyntaxKind.Member);
                afterBrace = false;
                continue;
            }
            afterBrace = c == '{' || afterBrace && char.IsWhiteSpace(c);
            position++;
        }
        if (closed) Add(position, end, SyntaxKind.String);
        position = end;
    }

    private int Name()
    {
        while (position < text.Length && IsNamePart(text[position])) position++;
        return position;
    }

    private void Add(int start, int end, SyntaxKind kind)
    {
        tokens.Add(new(start, end - start, kind));
        position = end;
    }

    private bool Follows(string opening) => text.AsSpan(position).StartsWith(opening, StringComparison.Ordinal);
    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or ':' or '-';
}
