namespace Trag.Domain.Merge;

// What one conflict, or a whole conflicted file, becomes: our side (the branch being merged into), their side (the
// incoming branch), or both one after the other. A whole file is only ever one side.
public enum ConflictChoice { Mine, Theirs, Both }
