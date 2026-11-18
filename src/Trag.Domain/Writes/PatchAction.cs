namespace Trag.Domain.Writes;

// What a patch of part of a file does: stage it into the index, take it back out, or take it out of the working tree.
public enum PatchAction
{
    Stage,
    Unstage,
    Discard
}
