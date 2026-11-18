using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Merge;

// One file of the merge or rebase stop whose content is decided, as its view lists it, with the sides named its way.
public sealed record ResolvedRow(MergeResolved Resolved, ConflictSides Sides)
{
    public string PathText => Resolved.Path.DisplayEscaped;
    public string Tag => MergeText.Tag(Resolved, Sides);
    public string Title => MergeText.ResolvedTitle(Resolved, Sides);
    public bool HasMarkers => Resolved.HasMarkers;
    public bool CanReopen => Resolved.CanReopen;
    // The tag's colour: markers are an error, the user's own side and the other side have theirs, the rest is neutral.
    public ResolvedKind Kind => Sides.KindOf(Resolved);
    public bool IsYours => Kind == ResolvedKind.Yours;
    public bool IsOther => Kind == ResolvedKind.Other;
    public bool IsEdited => Kind == ResolvedKind.Edited;
}
