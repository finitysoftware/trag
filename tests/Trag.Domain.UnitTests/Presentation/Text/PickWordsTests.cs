using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation.Text;

// Every word of the pick view names the operation Git applies: a cherry-pick its way, anything else as a revert.
public sealed class PickWordsTests
{
    [Fact]
    public void WhenACherryPickStopped_ShouldNameEveryActionAndTheQuestionForIt()
    {
        var words = new PickWords(RepositoryOperation.CherryPick);
        Assert.Equal(("Cherry-pick in progress", "Finish cherry-pick", "Abort cherry-pick…", "Abort this cherry-pick?", "Keep cherry-pick", "Abort this cherry-pick"),
            (words.Title, words.FinishAction, words.AbortAction, words.AbortTitle, words.KeepAction, words.ConfirmAbortAction));
        Assert.StartsWith("Finishing commits the cherry-pick", words.FinishHint);
        Assert.StartsWith("Git puts the branch and its files back as they were before the cherry-pick.", words.AbortQuestion);
    }

    [Fact]
    public void WhenARevertStopped_ShouldNameItARevert()
    {
        Assert.Equal("Revert in progress", new PickWords(RepositoryOperation.Revert).Title);
    }
}
