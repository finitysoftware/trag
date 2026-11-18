using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class LimitTextTests
{
    // The sentences that name a limit read it from the limit Trag applies, written as they always were.
    [Fact]
    public void WhenATextNamesALimit_ShouldWriteTheLimitTragApplies()
    {
        Assert.Equal("Limited preview · first 200 diff rows, long lines clipped.", FileDiffText.LimitedDiff);
        Assert.Equal("Limited preview · first 200 rows, long lines clipped.", FileDiffText.LimitedContent);
        Assert.Equal("Large diff · exceeds 5 MB, 50,000 rows or 8,192 characters per line.", FileDiffText.LargeDiff);
        Assert.Equal("Large content · exceeds 5 MB, 50,000 rows or 8,192 characters per line.", FileDiffText.LargeContent);
        Assert.Equal("Large file · exceeds 5 MB. Load a bounded content preview to inspect the beginning.", FileDiffText.LargeFileOffer);
        Assert.Equal("Content preview only · first 200 rows / 64 KB, long lines clipped. This is not a complete diff.", FileDiffText.ContentPreview);
        Assert.Equal("Conflicted working file · first 200 rows / 64 KB. Review conflict markers, edit externally, then stage the resolution.",
            FileDiffText.ConflictedWorkingFile);
        Assert.Equal("This file is larger than 2 MB. Use a side, the editor or the merge tool.", MergeText.TooLarge);
        Assert.Equal("Showing up to 10,000 changed files. This commit may contain more files.", CommitText.FileLimitNotice);
        Assert.Equal("Use a stash message of at most 1,024 characters without control characters, not starting with '-'.", StashOutcomeText.MessageInvalid);
        Assert.Equal("More than 5,000 upstream branches. Narrow the remote fetch configuration in Git.", SyncText.TooManyBranches);
        Assert.Equal("Trag supports up to 100 accounts. Remove an unused account before adding another.", AccountFailureText.TooMany(100));
    }
}
