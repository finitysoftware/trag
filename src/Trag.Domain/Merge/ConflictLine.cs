namespace Trag.Domain.Merge;

// One line of a conflicted file as the merge view shows it: its number in the working file and its text without the
// line ending.
public sealed record ConflictLine(int Number, string Text);
