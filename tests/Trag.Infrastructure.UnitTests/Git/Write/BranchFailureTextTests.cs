using System.Text;
using Trag.Infrastructure.Git.Write;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Git's own branch output, turned into the one sentence that tells the user what to do next.
public sealed class BranchFailureTextTests
{
    private static string Explain(string output) => BranchFailureText.Explain(new(1, [], Encoding.UTF8.GetBytes(output)));

    [Fact]
    public void WhenGitSaysLocalChangesWouldBeOverwritten_ShouldAskToCommitOrStashThemThenSwitchAgain()
    {
        const string expected = "Git kept the current branch: your local changes to the listed files would be overwritten. Commit or stash them, then switch again.";
        Assert.Equal(expected, Explain("error: Your local changes to the following files would be overwritten by checkout:\n\tsrc/app.cs"));
    }

    [Fact]
    public void WhenGitAsksToCommitOrStashBeforeSwitching_ShouldAskToCommitOrStashThemThenSwitchAgain()
    {
        const string expected = "Git kept the current branch: your local changes to the listed files would be overwritten. Commit or stash them, then switch again.";
        Assert.Equal(expected, Explain("Please commit your changes or stash them before you switch branches."));
    }

    // An untracked file is no local change a commit or a plain stash moves aside, so the sentence says to move it.
    [Fact]
    public void WhenGitSaysUntrackedFilesWouldBeOverwritten_ShouldAskToMoveThemOrStashThemWithUntrackedFiles()
    {
        const string expected = "Git kept the current branch: untracked files listed below would be overwritten by the branch's own. Move or remove them, or stash them with \"Include untracked files\", then switch again.";
        Assert.Equal(expected, Explain("error: The following untracked working tree files would be overwritten by checkout:\n\ttopic.txt\nPlease move or remove them before you switch branches.\nAborting"));
    }

    // A branch is checked out in one worktree at a time, so Git refuses it to every other; Git 2.43.0, 2.45.2, 2.50.0 and
    // 2.55.0 word the refusal of switch and checkout alike.
    [Fact]
    public void WhenGitSaysTheBranchIsUsedByAnotherWorktree_ShouldSayToSwitchThatWorktreeAwayOrRemoveIt()
    {
        const string expected = "Git kept the current branch: the branch you chose is checked out in another worktree. Switch that worktree to another branch or remove it, then switch again.";
        Assert.Equal(expected, Explain("fatal: 'feature' is already used by worktree at 'C:/repos/linked tree'"));
    }

    [Fact]
    public void WhenTheBranchNameIsTaken_ShouldOfferToChooseAnotherOrSwitchToIt() =>
        Assert.Equal("A branch with this name already exists. Choose another name or switch to it.",
            Explain("fatal: a branch named 'feature' already exists"));

    // A lock is waited out while a Git program runs; one that an interrupted Git left behind never goes away by waiting,
    // so the sentence names the file to remove. Trag itself never removes it.
    [Fact]
    public void WhenTheIndexIsLocked_ShouldSayToWaitForARunningGitOrRemoveTheLockOneLeftBehind() =>
        Assert.Equal("The Git index is locked. If a Git program is still running, wait for it to finish; if none is, an interrupted one left index.lock in the repository's Git folder: remove that file, then try again.",
            Explain("fatal: Unable to create '/repo/.git/index.lock': File exists."));

    [Fact]
    public void WhenGitSaysAnythingElse_ShouldAskToReviewTheDetailsBelow() =>
        Assert.Equal("Git could not complete the branch operation. Review the details below.",
            Explain("fatal: invalid reference: nowhere"));

    [Fact]
    public void WhenTheOutputMatchesTwoExplanations_ShouldGiveTheMostSpecificOne() =>
        // Local changes in the way and a taken name; the overwrite rule is checked first and answers.
        Assert.StartsWith("Git kept the current branch:",
            Explain("error: Your local changes would be overwritten by checkout\nfatal: a branch named 'feature' already exists"));
}
