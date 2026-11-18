namespace Trag.Domain.Blame;

// A file line by line as blame tells it, every line with the commit that last changed it. Git blames a binary file as if
// it had lines too; a NUL in them is how Git itself tells a binary file.
public sealed record FileBlame(IReadOnlyList<BlameLine> Lines)
{
    public bool IsBinary => Lines.Any(line => line.Text.Contains('\0'));
}
