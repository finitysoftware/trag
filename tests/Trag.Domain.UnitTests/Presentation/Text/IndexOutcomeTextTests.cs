using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What taking part of a file apart says: a hunk is named a hunk, and chosen lines that are not a whole hunk are named
// lines, on success and when Git refuses.
public sealed class IndexOutcomeTextTests
{
    [Fact]
    public void WhenAHunkOrChosenLinesAreTaken_ShouldNameWhatWasTaken()
    {
        Assert.Equal(["Hunk staged; the rest of the file is unchanged.", "Lines staged; the rest of the file is unchanged.",
                "Hunk unstaged; the working file is kept.", "Lines unstaged; the working file is kept.",
                "Hunk discarded; the rest of the file and what is staged are kept.", "Lines discarded; the rest of the file and what is staged are kept."],
            new[] { PatchAction.Stage, PatchAction.Unstage, PatchAction.Discard }.SelectMany(action => new[] { IndexOutcomeText.Taken(action, false), IndexOutcomeText.Taken(action, true) }));
    }

    [Fact]
    public void WhenGitRefusesAHunkOrChosenLines_ShouldNameWhatItRefused()
    {
        Assert.Equal("Git could not apply that hunk to the index; the file changed since its diff was shown. Refresh and try again.", IndexOutcomeText.NotTaken(PatchAction.Stage, false));
        Assert.Equal("Git could not apply those lines to the index; the file changed since its diff was shown. Refresh and try again.", IndexOutcomeText.NotTaken(PatchAction.Unstage, true));
        Assert.Equal("Git could not take that hunk out of the working file; the file changed since its diff was shown. Refresh and try again.", IndexOutcomeText.NotTaken(PatchAction.Discard, false));
        Assert.Equal("Git could not take those lines out of the working file; the file changed since its diff was shown. Refresh and try again.", IndexOutcomeText.NotTaken(PatchAction.Discard, true));
    }
}
