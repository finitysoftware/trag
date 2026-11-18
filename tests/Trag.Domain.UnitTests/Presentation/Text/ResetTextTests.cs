using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the reset question says about the commits leaving the branch and the work a hard reset loses, counted in words.
public sealed class ResetTextTests
{
    [Fact]
    public void WhenTheBranchGoesBackOneOrMoreCommits_ShouldSayHowFarFromWhereToWhere()
    {
        Assert.Equal("main moves back 1 commit, from aaaaaaa to bbbbbbb.", ResetText.Summary("main", 1, "aaaaaaa", "bbbbbbb"));
        Assert.Equal("main moves back 3 commits, from aaaaaaa to bbbbbbb.", ResetText.Summary("main", 3, "aaaaaaa", "bbbbbbb"));
    }

    [Fact]
    public void WhenLeavingCommitsAreOnTheUpstream_ShouldSayAPullWouldBringThemBack()
    {
        Assert.Equal("1 of these commits is already on origin/main; a Pull would bring it back.", ResetText.Pushed(1, "origin/main"));
        Assert.Equal("2 of these commits are already on origin/main; a Pull would bring them back.", ResetText.Pushed(2, "origin/main"));
    }

    // Untracked files are named because a hard reset leaves them, which is not what everyone expects.
    [Fact]
    public void WhenAHardResetIsChosen_ShouldSayWhatUncommittedWorkIsLost()
    {
        Assert.Equal("No uncommitted change is lost. Untracked files stay.", ResetText.HardLoss(0, []));
        Assert.Equal("1 changed file loses its uncommitted changes for good. Untracked files stay.", ResetText.HardLoss(1, []));
        Assert.Equal("2 changed files lose their uncommitted changes for good. Untracked files stay.", ResetText.HardLoss(2, []));
    }

    // The untracked files the commit's files take the place of are lost too, three by name and the rest counted.
    [Fact]
    public void WhenUntrackedFilesAreInTheWayOfTheCommitsFiles_ShouldNameThemAsLost()
    {
        Assert.Equal("1 changed file loses its uncommitted changes for good. The untracked file b\\\\c.txt is lost for good: the commit has a file in its place. Other untracked files stay.",
            ResetText.HardLoss(1, [Snapshots.Path("b\\c.txt")]));
        Assert.Equal("No uncommitted change is lost. 4 untracked files are lost for good, as the commit has files in their place: a, b, c and 1 more. Other untracked files stay.",
            ResetText.HardLoss(0, new[] { "a", "b", "c", "d" }.Select(Snapshots.Path).ToArray()));
    }
}
