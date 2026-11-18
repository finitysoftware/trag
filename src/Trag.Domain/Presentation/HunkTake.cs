namespace Trag.Domain.Presentation;

// The lines the button beside the changes takes, and the words for taking and for discarding them.
public sealed record HunkTake(IReadOnlyList<int> Lines, string Take, string Discard)
{
    public bool CanDiscard => Discard.Length != 0;
}
