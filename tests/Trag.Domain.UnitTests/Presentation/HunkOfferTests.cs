using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation;

// What the shown diff offers to take apart from the rest of its file: the words for a hunk and for chosen lines on each
// side, what the button beside the changes takes, and what the menu over them offers.
public sealed class HunkOfferTests
{
    // The button beside the changes stages with a plus and unstages with a minus; a discard shows only where it is offered.
    [Fact]
    public void WhenTheOfferIsShown_ShouldNameItsIconAndWhetherItDiscards()
    {
        Assert.Equal(("plus", "minus"), (HunkOffer.For(Snapshots.Modified("a"), false, true).Icon, HunkOffer.For(Snapshots.Staged("a"), true, true).Icon));
        Assert.True(new HunkTake([1], "Stage hunk", "Discard hunk").CanDiscard);
        Assert.False(new HunkTake([1], "Unstage hunk", "").CanDiscard);
    }

    [Fact]
    public void WhenALineIsAddedOrRemoved_ShouldBeAChange()
    {
        Assert.Equal([true, true, false, false], new PatchLine[] { new('+', "a"), new('-', "b"), new(' ', "c"), new('h', "@@") }.Select(line => line.IsChange));
    }

    // An unchanged line, a block of a removed and an added line (1-2), an unchanged line, a block of two added lines (4-5).
    private static readonly PatchLine[] Lines =
        [new(' ', "x"), new('-', "a"), new('+', "b"), new(' ', "y"), new('+', "c"), new('+', "d"), new(' ', "z")];

    private static readonly HunkOffer WorkingTree = HunkOffer.For(Snapshots.Modified("a.txt"), fromIndex: false, isCompleteText: true);
    private static readonly HunkOffer Index = HunkOffer.For(Snapshots.Staged("a.txt"), fromIndex: true, isCompleteText: true);

    [Fact]
    public void WhenAChangedFileIsShownWhole_ShouldOfferItsSidesWords()
    {
        Assert.Equal(new HunkOffer(StatusText.HunkAction(false), StatusText.LinesAction(false), StatusText.DiscardHunk, StatusText.DiscardLines, Unstages: false), WorkingTree);
        Assert.Equal(new HunkOffer(StatusText.HunkAction(true), StatusText.LinesAction(true), "", "", Unstages: true), Index);
    }

    // Part of a file cannot be taken from a diff that is not its whole text, nor from a file not changed on that side.
    [Fact]
    public void WhenTheDiffIsNotTheWholeTextOrTheFileDoesNotFit_ShouldOfferNothing()
    {
        Assert.Equal(HunkOffer.None, HunkOffer.For(Snapshots.Modified("a.txt"), fromIndex: false, isCompleteText: false));
        Assert.Equal(HunkOffer.None, HunkOffer.For(Snapshots.Untracked("a.txt"), fromIndex: false, isCompleteText: true));
        Assert.Null(HunkOffer.None.Button(Lines, [], new(4, 5)));
        Assert.Empty(HunkOffer.None.Menu(Lines, [4], new(4, 5)));
    }

    // The block under the pointer, unless lines of it are chosen; chosen lines elsewhere leave the pointed block to it.
    [Fact]
    public void WhenThePointerIsOverABlock_ShouldTakeItUnlessLinesOfItAreChosen()
    {
        Assert.Equal(("4,5", "Stage hunk", "Discard hunk"), Take(WorkingTree.Button(Lines, [], new(4, 5))));
        Assert.Equal(("4,5", "Stage hunk", "Discard hunk"), Take(WorkingTree.Button(Lines, [1], new(4, 5))));
        Assert.Equal(("4", "Stage lines", "Discard lines"), Take(WorkingTree.Button(Lines, [4], new(4, 5))));
    }

    // Lines that make a whole block are its hunk; without a pointer and a choice there is nothing to take.
    [Fact]
    public void WhenLinesAreChosenWithoutAPointer_ShouldTakeThemNamedForAHunkWhenTheyAreOne()
    {
        Assert.Equal(("1,2", "Unstage hunk", ""), Take(Index.Button(Lines, [1, 2], null)));
        Assert.Equal(("1", "Unstage lines", ""), Take(Index.Button(Lines, [1], null)));
        Assert.Null(WorkingTree.Button(Lines, [], null));
    }

    // The chosen lines unless they are a whole block, then the pointed hunk, each staged and then discarded where offered.
    [Fact]
    public void WhenTheMenuOpens_ShouldOfferTheChosenLinesAndTheHunkUnderThePointer()
    {
        Assert.Equal([("Stage lines", "4", false), ("Stage hunk", "1,2", false), ("Discard lines", "4", true), ("Discard hunk", "1,2", true)],
            Items(WorkingTree.Menu(Lines, [4], new(1, 2))));
        Assert.Equal([("Stage hunk", "4,5", false), ("Discard hunk", "4,5", true)], Items(WorkingTree.Menu(Lines, [4, 5], null)));
        Assert.Equal([("Unstage lines", "4", false), ("Unstage hunk", "4,5", false)], Items(Index.Menu(Lines, [4], null)));
        Assert.Empty(WorkingTree.Menu(Lines, [], null));
    }

    private static (string, string, string) Take(HunkTake? take) => take is { } some ? (string.Join(",", some.Lines), some.Take, some.Discard) : ("", "none", "none");

    private static (string, string, bool)[] Items(IReadOnlyList<HunkMenuItem> menu) => [.. menu.Select(item => (item.Header, string.Join(",", item.Lines), item.Discards))];
}
