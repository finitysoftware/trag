namespace Trag.Domain.Writes;

// What a reset does besides moving the branch: soft keeps the index and the working tree, mixed resets the index, hard
// the index and the tracked files too.
public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}
