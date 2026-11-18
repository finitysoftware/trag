using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation;

// What the shown diff offers to take apart from the rest of its file, and the words for it: a hunk or chosen lines,
// staged from the working tree or unstaged from the index, and discarded from the working tree after a question. None
// when the diff is not the file's whole text or the file is not changed on the shown side.
public sealed record HunkOffer(string Hunk, string Lines, string DiscardHunk, string DiscardLines, bool Unstages)
{
    public static readonly HunkOffer None = new("", "", "", "", false);
    // The icon of the button beside the changes: a plus stages, a minus unstages.
    public string Icon => Unstages ? "minus" : "plus";

    public static HunkOffer For(FileStatus file, bool fromIndex, bool isCompleteText) =>
        isCompleteText && HunkPatch.Fits(file, fromIndex)
            ? new(StatusText.HunkAction(fromIndex), StatusText.LinesAction(fromIndex), fromIndex ? "" : StatusText.DiscardHunk, fromIndex ? "" : StatusText.DiscardLines, fromIndex)
            : None;

    // The button beside the changes takes the block under the pointer unless lines of it are chosen, and otherwise the
    // chosen lines, named for a hunk when they make a whole block; null when there is nothing to take.
    public HunkTake? Button(IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen, DiffBlock? pointed)
    {
        var taken = pointed is { } block && !chosen.Any(line => line >= block.Start && line <= block.End) ? block.Lines : chosen;
        var (take, discard) = DiffBlocks.IsWhole(lines, taken) ? (Hunk, DiscardHunk) : (Lines, DiscardLines);
        return taken.Count == 0 || take.Length == 0 ? null : new(taken, take, discard);
    }

    // The menu offers the chosen lines unless they are a whole block, and the hunk under the pointer, or else the first
    // chosen line's, each staged and then discarded where that is offered; nothing without something to take.
    public IReadOnlyList<HunkMenuItem> Menu(IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen, DiffBlock? pointed)
    {
        var hunk = (pointed ?? (chosen.Count == 0 ? null : DiffBlocks.At(lines, chosen[0])))?.Lines;
        var part = chosen.Count == 0 || DiffBlocks.IsWhole(lines, chosen) ? null : chosen;
        var items = new List<HunkMenuItem>();
        void Add(string header, IReadOnlyList<int>? taken, bool discards)
        {
            if (taken is not null && header.Length != 0) items.Add(new(header, taken, discards));
        }
        Add(Lines, part, discards: false);
        Add(Hunk, hunk, discards: false);
        if (items.Count == 0) return [];
        Add(DiscardLines, part, discards: true);
        Add(DiscardHunk, hunk, discards: true);
        return items;
    }
}
