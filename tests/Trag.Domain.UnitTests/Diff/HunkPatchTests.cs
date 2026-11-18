using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Diff;

// A hunk is a run of removed and added lines between unchanged ones, and its patch is what git apply takes for that run
// alone: up to three unchanged lines around it, numbered on the side the patch is applied to. Chosen lines are patched
// the same way, from the first to the last; a change left out stays as it is on the side the patch is applied to.
public sealed class HunkPatchTests
{
    private static readonly GitPath Sample = new("docs/sample č.txt"u8.ToArray());

    // a b c d e f g h i, with e and i changed.
    private static readonly PatchLine[] TwoChanges =
    [
        new('h', "@@ -1,9 +1,9 @@"), new(' ', " a", 1, 1), new(' ', " b", 2, 2), new(' ', " c", 3, 3), new(' ', " d", 4, 4),
        new('-', "-e", 5), new('+', "+E", null, 5), new(' ', " f", 6, 6), new(' ', " g", 7, 7), new(' ', " h", 8, 8),
        new('-', "-i", 9), new('+', "+I", null, 9)
    ];

    [Fact]
    public void WhenAChangedLineIsChosen_ShouldFindItsWholeRunOfChanges()
    {
        Assert.Equal(new DiffBlock(5, 6), DiffBlocks.At(TwoChanges, 5));
        Assert.Equal(new DiffBlock(5, 6), DiffBlocks.At(TwoChanges, 6));
        Assert.Equal(new DiffBlock(10, 11), DiffBlocks.At(TwoChanges, 11));
        Assert.Null(DiffBlocks.At(TwoChanges, 4)); Assert.Null(DiffBlocks.At(TwoChanges, 0)); Assert.Null(DiffBlocks.At(TwoChanges, 12));
    }

    [Fact]
    public void WhenChosenLinesAreExactlyTheChangesOfOneRun_ShouldCountAsItsHunk()
    {
        Assert.True(DiffBlocks.IsWhole(TwoChanges, [5, 6]));
        Assert.True(DiffBlocks.IsWhole(TwoChanges, [10, 11]));
        Assert.False(DiffBlocks.IsWhole(TwoChanges, [6]));
        Assert.False(DiffBlocks.IsWhole(TwoChanges, [5, 6, 10, 11]));
    }

    // Staged, the removed e is not chosen: the index keeps it, so it is context, and E goes in after it.
    [Fact]
    public void WhenOnlyTheAddedLineOfAChangeIsStaged_ShouldKeepTheRemovedOneAsContext()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -3,6 +3,7 @@\n c\n d\n e\n+E\n f\n g\n h\n",
            HunkPatch.For(TwoChanges, [6], Sample, PatchAction.Stage));
    }

    // The added E is not chosen: the index never had it, so it is left out, and nothing counts it as context.
    [Fact]
    public void WhenOnlyTheRemovedLineOfAChangeIsStaged_ShouldLeaveTheAddedOneOut()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -2,7 +2,6 @@\n b\n c\n d\n-e\n f\n g\n h\n",
            HunkPatch.For(TwoChanges, [5], Sample, PatchAction.Stage));
    }

    // In reverse it is the other way round: the side the patch is applied to has the added lines and not the removed.
    [Theory]
    [InlineData(PatchAction.Unstage)]
    [InlineData(PatchAction.Discard)]
    public void WhenOnlyTheAddedLineOfAChangeIsTakenBack_ShouldLeaveTheRemovedOneOut(PatchAction action)
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -2,6 +2,7 @@\n b\n c\n d\n+E\n f\n g\n h\n",
            HunkPatch.For(TwoChanges, [6], Sample, action));
    }

    [Fact]
    public void WhenOnlyTheRemovedLineOfAChangeIsTakenBack_ShouldKeepTheAddedOneAsContext()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -2,7 +2,6 @@\n b\n c\n d\n-e\n E\n f\n g\n",
            HunkPatch.For(TwoChanges, [5], Sample, PatchAction.Unstage));
    }

    // Lines of two runs make one hunk: what lies between is context, the removed i too.
    [Fact]
    public void WhenLinesOfTwoChangesAreChosen_ShouldPatchThemAsOneHunkWithTheLinesBetweenAsContext()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -3,7 +3,9 @@\n c\n d\n e\n+E\n f\n g\n h\n i\n+I\n",
            HunkPatch.For(TwoChanges, [6, 11], Sample, PatchAction.Stage));
    }

    [Fact]
    public void WhenAHunkIsStaged_ShouldPatchItAloneWithThreeLinesAroundItNumberedLikeTheIndex()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -2,7 +2,7 @@\n b\n c\n d\n-e\n+E\n f\n g\n h\n",
            HunkPatch.For(TwoChanges, new DiffBlock(5, 6).Lines, Sample, PatchAction.Stage));
    }

    // Unstaged, the patch is applied in reverse to the index, which is the diff's new side; a hunk above that stays
    // staged has moved the lines there.
    [Fact]
    public void WhenAHunkIsUnstaged_ShouldNumberItLikeTheIndexOnTheNewSide()
    {
        PatchLine[] staged = [new(' ', " a", 1, 1), new('+', "+x", null, 2), new(' ', " b", 2, 3), new(' ', " c", 3, 4), new(' ', " d", 4, 5), new('-', "-e", 5), new('+', "+E", null, 6)];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -3,4 +3,4 @@\n b\n c\n d\n-e\n+E\n",
            HunkPatch.For(staged, new DiffBlock(5, 6).Lines, Sample, PatchAction.Unstage));
    }

    // Discarded, the patch is applied in reverse to the working tree, which is the diff's new side; a hunk above that is
    // kept has moved the lines there.
    [Fact]
    public void WhenAHunkIsDiscarded_ShouldNumberItLikeTheWorkingTreeOnTheNewSide()
    {
        PatchLine[] changed = [new(' ', " a", 1, 1), new('+', "+x", null, 2), new(' ', " b", 2, 3), new(' ', " c", 3, 4), new(' ', " d", 4, 5), new('-', "-e", 5), new('+', "+E", null, 6)];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -3,4 +3,4 @@\n b\n c\n d\n-e\n+E\n",
            HunkPatch.For(changed, new DiffBlock(5, 6).Lines, Sample, PatchAction.Discard));
    }

    // A line Git ends with CRLF keeps its CR, and a last line without a newline says so the way Git does.
    [Fact]
    public void WhenLinesEndWithCrlfOrTheLastHasNoNewline_ShouldKeepThatInThePatch()
    {
        PatchLine[] lines = [new(' ', " a", 1, 1) { HasCarriageReturn = true }, new('-', "-b", 2) { NoNewline = true }, new('+', "+B", null, 2) { NoNewline = true }];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -1,2 +1,2 @@\n a\r\n-b\n\\ No newline at end of file\n+B\n\\ No newline at end of file\n",
            HunkPatch.For(lines, new DiffBlock(1, 2).Lines, Sample, PatchAction.Stage));
    }

    // A hunk is taken alone only from a file changed on that side, by a name Git writes into a patch as it is; a new,
    // deleted or conflicted file is staged whole.
    [Fact]
    public void WhenAFileIsChangedOnTheShownSide_ShouldLetItsHunksBeTakenAlone()
    {
        Assert.True(HunkPatch.Fits(Snapshots.Modified("a.txt"), staged: false));
        Assert.False(HunkPatch.Fits(Snapshots.Modified("a.txt"), staged: true));
        Assert.True(HunkPatch.Fits(Snapshots.PartiallyStaged("a.txt"), staged: true));
        Assert.False(HunkPatch.Fits(Snapshots.Untracked("new.txt"), staged: false));
        Assert.False(HunkPatch.Fits(Snapshots.Staged("added.txt", 'A'), staged: true));
        Assert.False(HunkPatch.Fits(Snapshots.Conflict("both.txt"), staged: false));
        Assert.False(HunkPatch.Fits(Snapshots.Modified("say \"hi\".txt"), staged: false));
    }

    // A link that became a file, or a file that became a link, is a change of type a patch of lines cannot carry.
    [Fact]
    public void WhenTheRowIsATypeChange_ShouldLetNoHunkBeTakenAlone()
    {
        Assert.False(HunkPatch.Fits(Snapshots.Modified("link") with { Worktree = 'T' }, staged: false));
        Assert.False(HunkPatch.Fits(Snapshots.Staged("link", 'T'), staged: true));
    }

    // A name Git writes quoted into a patch, with a backslash or a control character as with a quote, is offered no hunk;
    // spaces at either end Git writes as they are, and git apply keeps them part of the name.
    [Theory]
    [InlineData("a\\b.txt", false)]
    [InlineData("a\tb.txt", false)]
    [InlineData(" lead.txt", true)]
    [InlineData("trail.txt ", true)]
    public void WhenThePathHasQuotesBackslashTabOrEdgeSpaces_ShouldOfferAHunkOnlyForTheSpaces(string path, bool fits) =>
        Assert.Equal(fits, HunkPatch.Fits(Snapshots.Modified(path), staged: false));

    [Fact]
    public void WhenTheHunkIsTheLastLinesOfTheFile_ShouldPatchNothingAfterIt()
    {
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -6,4 +6,4 @@\n f\n g\n h\n-i\n+I\n",
            HunkPatch.For(TwoChanges, new DiffBlock(10, 11).Lines, Sample, PatchAction.Stage));
    }

    // Staged, the other change is context as the index has it: its removed line stays and its added one is left out.
    [Fact]
    public void WhenAnotherChangeIsOneLineAway_ShouldTakeItAsContextTheWayTheIndexHasIt()
    {
        PatchLine[] close = [new(' ', " d", 4, 4), new('-', "-e", 5), new('+', "+E", null, 5), new(' ', " f", 6, 6), new('-', "-g", 7), new('+', "+G", null, 7), new(' ', " h", 8, 8)];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -4,5 +4,5 @@\n d\n-e\n+E\n f\n g\n h\n",
            HunkPatch.For(close, new DiffBlock(1, 2).Lines, Sample, PatchAction.Stage));
    }

    // A last line without a newline, changed: b becomes B. Staging B alone keeps b, which then ends with a newline in the
    // index, so it is the old side's last line only and is written once as removed and once as added.
    [Fact]
    public void WhenOnlyTheAddedLineOfALastLineWithoutANewlineIsStaged_ShouldEndTheOldLineOnTheOldSideOnly()
    {
        PatchLine[] last = [new(' ', " a", 1, 1), new('-', "-b", 2) { NoNewline = true }, new('+', "+B", null, 2) { NoNewline = true }];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -1,2 +1,3 @@\n a\n-b\n\\ No newline at end of file\n+b\n+B\n\\ No newline at end of file\n",
            HunkPatch.For(last, [2], Sample, PatchAction.Stage));
    }

    // Discarding b's removal alone brings b back before B, so b ends with a newline on the side the discard gives back.
    [Fact]
    public void WhenOnlyTheRemovedLineOfALastLineWithoutANewlineIsDiscarded_ShouldGiveItANewline()
    {
        PatchLine[] last = [new(' ', " a", 1, 1), new('-', "-b", 2) { NoNewline = true }, new('+', "+B", null, 2) { NoNewline = true }];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -1,3 +1,2 @@\n a\n-b\n B\n\\ No newline at end of file\n",
            HunkPatch.For(last, [1], Sample, PatchAction.Discard));
    }

    // A file added or renamed in the index and changed since has hunks of its working tree side only.
    [Fact]
    public void WhenTheFileIsAddedOrRenamedAndModified_ShouldLetHunksBeTakenFromTheWorkingTreeSideOnly()
    {
        foreach (var file in new[] { Snapshots.Staged("added.txt", 'A') with { Worktree = 'M' }, Snapshots.Staged("renamed.txt", 'R') with { Worktree = 'M' } })
        {
            Assert.True(HunkPatch.Fits(file, staged: false));
            Assert.False(HunkPatch.Fits(file, staged: true));
        }
    }

    // Added before the first line: nothing above it, and the old side starts at the line it goes before.
    [Fact]
    public void WhenAHunkStartsTheFile_ShouldPatchFromTheFirstLine()
    {
        PatchLine[] lines = [new('+', "+new", null, 1), new(' ', " a", 1, 2), new(' ', " b", 2, 3)];
        Assert.Equal("--- a/docs/sample č.txt\n+++ b/docs/sample č.txt\n@@ -1,2 +1,3 @@\n+new\n a\n b\n", HunkPatch.For(lines, new DiffBlock(0, 0).Lines, Sample, PatchAction.Stage));
    }
}
