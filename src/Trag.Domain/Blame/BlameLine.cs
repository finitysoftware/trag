namespace Trag.Domain.Blame;

// One line of a blamed file: its number, its text, and the commit that last changed it.
public sealed record BlameLine(int Number, string Text, BlameCommit Commit);
