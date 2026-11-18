namespace Trag.Domain.Merge;

// One piece of a conflicted file as the merge view shows it: a few unchanged lines around the conflicts, or one conflict
// with both sides. Block is the conflict's index for the choice made on it (-1 for unchanged lines); GapBefore says
// lines were left out before this piece.
public sealed record ConflictPart(IReadOnlyList<ConflictLine> Lines, bool GapBefore, int Block = -1, string Title = "",
    IReadOnlyList<ConflictLine>? Mine = null, IReadOnlyList<ConflictLine>? Theirs = null)
{
    public bool IsBlock => Block >= 0;
}
