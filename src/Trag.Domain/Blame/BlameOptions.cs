namespace Trag.Domain.Blame;

// How Git looks past a change when it blames: whitespace ignored (-w), and lines moved within the file or copied from
// another file of the same commit kept on the commit that wrote them (-C, which takes in -M).
public sealed record BlameOptions(bool IgnoreWhitespace, bool DetectMoves)
{
    public static readonly BlameOptions None = new(false, false);
}
