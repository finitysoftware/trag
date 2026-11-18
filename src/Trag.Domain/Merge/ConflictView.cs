using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Merge;

// A conflicted file as the merge view shows it: its conflicts with the lines around them and the hash a choice is made
// on, or why it is not shown that way.
public sealed record ConflictView(IReadOnlyList<ConflictPart> Parts, string Hash, string Message)
{
    public static readonly ConflictView None = new([], "", "");
    public bool HasMessage => Message.Length != 0;

    public static ConflictView From(ConflictText text)
    {
        if (!text.IsReadable) return new([], "", text.Message);
        if (ConflictDocument.Parse(text.Text, text.MarkerSize) is not { } document)
            return new([], "", MergeText.MarkersIncomplete);
        return document.Blocks.Count == 0
            ? new([], text.Hash, MergeText.NoBlockLeft)
            : new(document.Parts(), text.Hash, "");
    }
}
