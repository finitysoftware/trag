namespace Trag.Domain.Diff;

public sealed record FileDiff(IReadOnlyList<FileDiffMetadata> Metadata, IReadOnlyList<PatchLine> Lines,
    DiffContentKind Kind, string Message, bool CanPreview = false)
{
    // The text a Git LFS pointer stands for, which is shown in place of the pointers Git holds.
    public bool IsLfsContent { get; init; }

    // Two reads of one file show the same thing: a refreshed diff that did not change keeps the one on screen.
    public bool HasSameContent(FileDiff other) => Kind == other.Kind && Message == other.Message && CanPreview == other.CanPreview
        && Metadata.Count == other.Metadata.Count && Metadata.Zip(other.Metadata).All(pair => pair.First.HasSameContent(pair.Second))
        && Lines.SequenceEqual(other.Lines);
}
