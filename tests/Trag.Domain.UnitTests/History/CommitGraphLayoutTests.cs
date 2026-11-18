using Trag.Domain.History;

namespace Trag.Domain.UnitTests.History;

public sealed class CommitGraphLayoutTests
{
    [Fact]
    public void WhenTheHistoryIsLinear_ShouldUseOneLaneThatEndsAtTheRoot()
    {
        var graph = CommitGraphLayout.Build([Commit("c", "b"), Commit("b", "a"), Commit("a")]);
        Assert.Equal(1, graph.LaneCount);
        var top = graph.For(Id("c"))!;
        Assert.Empty(top.Incoming); Assert.Equal([new GraphEdge(0, 0, 0)], top.Outgoing);
        var root = graph.For(Id("a"))!;
        Assert.Equal([new GraphEdge(0, 0, 0)], root.Incoming); Assert.Empty(root.Outgoing);
    }

    [Fact]
    public void WhenACommitMergesABranch_ShouldOpenASecondLaneThatCurvesBackIntoTheFirstParentLine()
    {
        // m merges feature f into base b; b is the common parent.
        var graph = CommitGraphLayout.Build([Commit("m", "d", "f"), Commit("f", "b"), Commit("d", "b"), Commit("b")]);
        Assert.Equal(2, graph.LaneCount);
        var merge = graph.For(Id("m"))!;
        Assert.Equal(0, merge.Lane);
        Assert.Contains(new GraphEdge(0, 0, 0), merge.Outgoing);
        Assert.Contains(merge.Outgoing, edge => edge.FromLane == 0 && edge.ToLane == 1 && edge.Color == 1);
        var feature = graph.For(Id("f"))!;
        Assert.Equal(1, feature.Lane); Assert.Equal(1, feature.Color);
        Assert.Contains(new GraphEdge(1, 1, 1), feature.Incoming); Assert.Contains(new GraphEdge(0, 0, 0), feature.Incoming);
        Assert.Contains(new GraphEdge(1, 1, 1), graph.For(Id("d"))!.Outgoing);
        var baseCommit = graph.For(Id("b"))!;
        Assert.Contains(baseCommit.Incoming, edge => edge.FromLane == 1 && edge.ToLane == 0);
        Assert.Contains(new GraphEdge(0, 0, 0), baseCommit.Incoming);
        Assert.Empty(baseCommit.Outgoing);
    }

    [Fact]
    public void WhenTipsAreIndependent_ShouldGiveEachItsOwnLaneAndReuseFreedLanes()
    {
        var graph = CommitGraphLayout.Build([Commit("x", "p"), Commit("y", "q"), Commit("p"), Commit("z", "q"), Commit("q")]);
        Assert.Equal(2, graph.LaneCount);
        Assert.Equal(0, graph.For(Id("x"))!.Lane); Assert.Equal(1, graph.For(Id("y"))!.Lane);
        Assert.Equal(0, graph.For(Id("z"))!.Lane);
        Assert.Contains(graph.For(Id("q"))!.Incoming, edge => edge.ToLane == graph.For(Id("q"))!.Lane);
        Assert.Equal(5, graph.Rows.Count);
    }

    // An octopus merge opens a lane for each parent after the first, each with its own color, and every lane closes
    // into its parent's row.
    [Fact]
    public void WhenACommitHasThreeParents_ShouldOpenALaneForEachExtraParentAndCloseThemAtTheirCommits()
    {
        var graph = CommitGraphLayout.Build([Commit("o", "a", "b", "c"), Commit("c", "r"), Commit("b", "r"), Commit("a", "r"), Commit("r")]);

        var octopus = graph.For(Id("o"))!;
        Assert.Equal(3, graph.LaneCount);
        Assert.Equal([0, 1, 2], octopus.Outgoing.Select(edge => edge.ToLane).Order());
        Assert.Equal(3, octopus.Outgoing.Select(edge => edge.Color).Distinct().Count());
        Assert.Equal((2, 1, 0), (graph.For(Id("c"))!.Lane, graph.For(Id("b"))!.Lane, graph.For(Id("a"))!.Lane));
        var root = graph.For(Id("r"))!;
        Assert.Equal(0, root.Lane);
        Assert.All(root.Incoming, edge => Assert.Equal(0, edge.ToLane));
        Assert.Empty(root.Outgoing);
    }

    // Two merges that cross, each merging the other's tip: the second joins each of its parents into the lane already
    // waiting for it, so neither parent gets a second lane.
    [Fact]
    public void WhenTwoMergesCrissCross_ShouldJoinEachParentIntoTheLaneAlreadyWaitingForIt()
    {
        var graph = CommitGraphLayout.Build([Commit("x", "a", "b"), Commit("y", "b", "a"), Commit("b", "r"), Commit("a", "r"), Commit("r")]);

        var second = graph.For(Id("y"))!;
        Assert.Equal(3, graph.LaneCount);
        Assert.Equal([(2, 0), (2, 1)], second.Outgoing.Where(edge => edge.FromLane == 2).Select(edge => (edge.FromLane, edge.ToLane)).Order());
        Assert.Equal((1, 0), (graph.For(Id("b"))!.Lane, graph.For(Id("a"))!.Lane));
        Assert.All(graph.For(Id("r"))!.Incoming, edge => Assert.Equal(0, edge.ToLane));
    }

    // A root in the middle of the page frees its lane, and the next tip takes it instead of opening another.
    [Fact]
    public void WhenARootSitsInTheMiddleOfAPage_ShouldFreeItsLaneForTheNextTip()
    {
        var graph = CommitGraphLayout.Build([Commit("x", "p"), Commit("p"), Commit("y", "q"), Commit("q")]);

        Assert.Equal(1, graph.LaneCount);
        Assert.Equal((0, 0), (graph.For(Id("p"))!.Lane, graph.For(Id("y"))!.Lane));
        Assert.Empty(graph.For(Id("p"))!.Outgoing);
    }

    // A commit the page lists twice is drawn once, where it came first.
    [Fact]
    public void WhenAMergeIsSeenTwiceOnOnePage_ShouldIgnoreTheDuplicateRow()
    {
        var graph = CommitGraphLayout.Build([Commit("m", "a", "b"), Commit("m", "a", "b"), Commit("b", "a"), Commit("a")]);

        Assert.Equal(3, graph.Rows.Count);
        Assert.Equal(0, graph.For(Id("m"))!.Lane);
    }

    // Lanes still waiting for a parent below the page keep their outgoing edges and count in its width.
    [Fact]
    public void WhenThePageEndsWithUnresolvedLanes_ShouldKeepTheirOutgoingEdgesAndLaneCount()
    {
        var graph = CommitGraphLayout.Build([Commit("m", "a", "b"), Commit("b", "z")]);

        Assert.Equal(2, graph.LaneCount);
        var last = graph.For(Id("b"))!;
        Assert.Contains(new GraphEdge(1, 1, last.Color), last.Outgoing);
        Assert.Contains(last.Outgoing, edge => edge.FromLane == 0 && edge.ToLane == 0);
    }

    // Eight short branches open one after another next to a lane still waiting below the page: each takes its column's
    // color, so the eighth is not drawn in the color of the lane beside it.
    [Fact]
    public void WhenShortBranchesKeepOpeningNextToALane_ShouldColourEachLaneByItsColumn()
    {
        string[] tips = ["1", "2", "3", "4", "5", "6", "7", "8"];
        var graph = CommitGraphLayout.Build([Commit("x", "z"), .. tips.Select(tip => Commit(tip))]);

        Assert.Equal((0, 0), (graph.For(Id("x"))!.Lane, graph.For(Id("x"))!.Color));
        Assert.All(tips, tip => Assert.Equal((1, 1), (graph.For(Id(tip))!.Lane, graph.For(Id(tip))!.Color)));
    }

    // A branch whose first parent already waits in a lane to its left curves into that lane in that lane's color; x keeps
    // lane 0 busy so the lane joined is not the one whose color is 0.
    [Fact]
    public void WhenABranchJoinsALaneToItsLeft_ShouldDrawTheCurveInTheColorOfTheLaneItJoins()
    {
        var graph = CommitGraphLayout.Build([Commit("x", "z"), Commit("m", "d", "f"), Commit("f", "d"), Commit("d")]);

        Assert.Contains(new GraphEdge(2, 1, 1), graph.For(Id("f"))!.Outgoing);
    }

    // The history read on in batches, as scrolling down reads it: the lanes open at the end of the first batch go on into
    // the second, so every row is drawn as in the graph of the whole history at once.
    [Fact]
    public void WhenTheHistoryIsAddedInBatches_ShouldDrawEveryRowAsTheWholeHistoryAtOnce()
    {
        HistoryCommit[] history = [Commit("m", "d", "f"), Commit("f", "e"), Commit("d", "c"), Commit("e", "b"), Commit("c", "b"), Commit("b", "a"), Commit("a")];
        var whole = CommitGraphLayout.Build(history);
        var layout = new CommitGraphLayout();

        layout.Add(history[..3]);
        var batched = layout.Add(history[3..]);

        Assert.Equal(whole.LaneCount, batched.LaneCount);
        Assert.All(history, commit => Assert.Equivalent(whole.For(commit.Id), batched.For(commit.Id), strict: true));
    }

    private static string Id(string key) => new(key[0], 40);
    private static HistoryCommit Commit(string key, params string[] parents) =>
        new(Id(key), "Author", "author@example.invalid", "2026-09-14T12:00:00+00:00", key) { Parents = parents.Select(Id).ToArray() };
}
