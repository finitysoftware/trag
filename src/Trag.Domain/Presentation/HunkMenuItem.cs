namespace Trag.Domain.Presentation;

// One entry of the menu over the changes: its words, the lines it takes, and whether it discards them.
public sealed record HunkMenuItem(string Header, IReadOnlyList<int> Lines, bool Discards);
