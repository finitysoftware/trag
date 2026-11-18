using Trag.Domain.Presentation;

namespace Trag.Domain.Blame;

// A line as the blame view shows it: the line, whether it starts a block of lines one commit last changed, the commit's
// label on that first line only, and the commit's age among the file's commits, from 0 for the oldest to the newest.
public sealed record BlameRow(BlameLine Line, bool StartsBlock, int Age, string Label)
{
    public SyntaxToken[] Tokens { get; init; } = [];
}
