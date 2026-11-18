namespace Trag.Domain.Diff;

// A run of removed and added lines between unchanged ones, by the indices of its first and last line in the diff.
public readonly record struct DiffBlock(int Start, int End)
{
    public IReadOnlyList<int> Lines => Enumerable.Range(Start, End - Start + 1).ToArray();
}
