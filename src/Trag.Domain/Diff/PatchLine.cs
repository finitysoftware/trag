namespace Trag.Domain.Diff;

public sealed record PatchLine(char Kind, string Text, int? OldNumber = null, int? NewNumber = null)
{
    public bool HasCarriageReturn { get; init; }
    public bool NoNewline { get; init; }
    public bool IsAddition => Kind == '+';
    public bool IsDeletion => Kind == '-';
    public bool IsChange => Kind is '+' or '-';
    public bool IsHeader => Kind == 'h';

    // Without LF, a trailing CR belongs to the content rather than to a CRLF terminator.
    public PatchLine WithoutTerminator() => this with
    {
        NoNewline = true,
        HasCarriageReturn = false,
        Text = HasCarriageReturn ? Text + "\r" : Text
    };
}
