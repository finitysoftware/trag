using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.History;

// The feed's filter as a page query.
public sealed class HistoryFilterTests
{
    [Theory]
    [InlineData(1024, false, true)]
    [InlineData(1025, false, false)]
    [InlineData(10, true, false)]
    public void WhenASearchIsApplied_ShouldValidateItAndCarryItIntoThePageQuery(int length, bool nul, bool valid)
    {
        var text = new string(nul ? '\0' : 'a', length);
        var filter = new HistoryFilter(text, true, true);
        Assert.Equal(valid, filter.ValidationMessage.Length == 0);
        Assert.Equal(new HistoryQuery(text, 3, true), filter.ForPage(3));
    }

    // Git leaves out the commits that do not change the file, so a graph drawn from those it lists would show lines that
    // are not the history's.
    [Fact]
    public void WhenTheFeedFollowsAFile_ShouldAskEveryPageForItSayWhichFileAndDrawNoGraph()
    {
        var filter = new HistoryFilter("", true, false) { Path = "src/a.txt" };
        var page = ShownHistoryPage.From(new([Snapshots.Commit(Snapshots.Id('b'), "Change a", Snapshots.Id('a')), Snapshots.Commit(Snapshots.Id('a'), "Add a")], false, 0), new CommitGraphLayout(), filter, false);

        Assert.Equal(new HistoryQuery("", 2, true) { Path = "src/a.txt" }, filter.ForPage(2));
        Assert.Same(CommitGraph.Empty, page.Graph);
        Assert.Equal("History of src/a.txt on all branches · newest commits first", page.Message);
        Assert.Equal("No commits on the current branch change src/a.txt.", (filter with { AllBranches = false }).Describe(new([], false, 0), false));
    }

    // An unborn branch has nothing of its own to list, but the commits of the other branches are all branches' history.
    [Fact]
    public void WhenTheBranchIsUnbornAndOtherBranchesHaveCommits_ShouldDescribeTheirHistoryAsAllBranches()
    {
        var filter = new HistoryFilter("", true, false);

        Assert.Equal("All branches · newest commits first", filter.Describe(new([Snapshots.Commit(Snapshots.Id('a'), "On main")], false, 0), true));
        Assert.Equal("This branch has no commits yet.", filter.Describe(new([], false, 0), true));
    }

    // A search of all branches on an unborn branch looks through the other branches' commits, so an empty page means no
    // commit matched, not that the branch has none.
    [Fact]
    public void WhenTheBranchIsUnbornAndASearchOfAllBranchesFindsNothing_ShouldSayNoCommitMatches()
    {
        var filter = new HistoryFilter("fix", true, true);

        Assert.Equal("No commits match this query on all branches.", filter.Describe(new([], false, 0), true));
    }

    [Fact]
    public void WhenTheBranchIsUnbornAndNoBranchChangesTheFile_ShouldSayNoCommitChangesIt()
    {
        var filter = new HistoryFilter("", true, false) { Path = "src/a.txt" };

        Assert.Equal("No commits on all branches change src/a.txt.", filter.Describe(new([], false, 0), true));
    }

    // A search lists only the commits that match, whose parents are mostly not on the page, so each of them would open a
    // lane of its own.
    [Fact]
    public void WhenThePageIsASearch_ShouldDrawNoGraph()
    {
        var filter = new HistoryFilter("fix", true, true);
        var page = ShownHistoryPage.From(new([Snapshots.Commit(Snapshots.Id('c'), "Fix b", Snapshots.Id('b')), Snapshots.Commit(Snapshots.Id('a'), "Fix a", Snapshots.Id('0'))], false, 0), new CommitGraphLayout(), filter, false);

        Assert.Same(CommitGraph.Empty, page.Graph);
    }
}
