using Trag.Domain.Merge;

namespace Trag.Domain.UnitTests.Merge;

// How a resolved conflict was resolved, from the blob staged for it against the two sides: a side's own blob is that
// side; nothing staged keeps the side that had the file; anything else is the user's edit.
public sealed class ResolutionTests
{
    [Fact]
    public void WhenTheStagedBlobIsASidesOwn_ShouldBeThatSideAndAnythingElseAnEdit()
    {
        Assert.Equal(Resolution.Mine, MergeResolutions.How("m", "t", staged: "m"));
        Assert.Equal(Resolution.Theirs, MergeResolutions.How("m", "t", staged: "t"));
        Assert.Equal(Resolution.Edited, MergeResolutions.How("m", "t", staged: "e"));
    }

    [Fact]
    public void WhenNothingIsStaged_ShouldKeepTheSideThatHadNoFileOrBeAnEdit()
    {
        Assert.Equal(Resolution.Mine, MergeResolutions.How(null, "t", staged: null));
        Assert.Equal(Resolution.Theirs, MergeResolutions.How("m", null, staged: null));
        Assert.Equal(Resolution.Edited, MergeResolutions.How("m", "t", staged: null));
    }
}
