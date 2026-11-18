using Trag.Domain.Diff;

namespace Trag.Domain.Presentation;

// What the diff pane shows: the presentation, the message above it and whether a limited preview can be loaded. An
// empty document carries the source being read so the view keeps its place until the content arrives.
public sealed record DiffDocument(DiffPresentation Presentation, string Message, bool CanPreview)
{
    public IReadOnlyList<PatchLine> Lines => Presentation.Lines;
    public bool IsCompleteText { get; init; }
    public static DiffDocument Empty(string message, DiffSource? source = null) =>
        new(source is null ? DiffPresentation.Empty : DiffPresentation.Empty with { Source = source }, message, false);
    // Only a diff read whole as text can be cut into hunks, and only one of what Git holds: an LFS file's content is not.
    public static DiffDocument From(FileDiff diff, DiffPresentation presentation) =>
        new(presentation, diff.Message, diff.CanPreview) { IsCompleteText = diff.Kind == DiffContentKind.Text && !diff.IsLfsContent };
}
