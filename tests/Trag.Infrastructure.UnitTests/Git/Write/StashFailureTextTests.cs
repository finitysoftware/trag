using System.Text;
using Trag.Infrastructure.Git.Write;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Git's own stash output, turned into the one sentence that tells the user what to do next.
public sealed class StashFailureTextTests
{
    private static string Explain(string output) => StashFailureText.Explain(new(1, [], Encoding.UTF8.GetBytes(output)));

    [Fact]
    public void WhenGitSaysLocalChangesWouldBeOverwritten_ShouldAskToCommitOrStashThemFirst()
    {
        const string expected = "Git kept the working tree: local changes to the listed files would be overwritten. Commit or stash them first.";
        Assert.Equal(expected, Explain("error: Your local changes to the following files would be overwritten by merge:\n\tsrc/app.cs"));
    }

    [Fact]
    public void WhenGitAsksToCommitOrStashBeforeMerging_ShouldAskToCommitOrStashThemFirst()
    {
        const string expected = "Git kept the working tree: local changes to the listed files would be overwritten. Commit or stash them first.";
        Assert.Equal(expected, Explain("Please commit your changes or stash them before you merge."));
    }

    // A lock is waited out while a Git program runs; one that an interrupted Git left behind never goes away by waiting,
    // so the sentence names the file to remove. Trag itself never removes it.
    [Fact]
    public void WhenTheIndexIsLocked_ShouldSayToWaitForARunningGitOrRemoveTheLockOneLeftBehind() =>
        Assert.Equal("The Git index is locked. If a Git program is still running, wait for it to finish; if none is, an interrupted one left index.lock in the repository's Git folder: remove that file, then try again.",
            Explain("fatal: Unable to create '/repo/.git/index.lock': File exists."));

    [Fact]
    public void WhenGitSaysAnythingElse_ShouldAskToReviewTheDetailsBelow() =>
        Assert.Equal("Git could not complete the stash operation. Review the details below.",
            Explain("fatal: log for refs/stash is empty"));

    [Fact]
    public void WhenTheOutputMatchesTwoExplanations_ShouldGiveTheMostSpecificOne() =>
        // Local changes in the way and files already in place; the overwrite rule is checked first and answers.
        Assert.StartsWith("Git kept the working tree:",
            Explain("error: Your local changes would be overwritten by merge\nsrc/new.cs already exists, no checkout"));
}
