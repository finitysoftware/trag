using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// What a rebase would do, read from Git before anything changes; one commit or one file is already enough to count.
public sealed class RebasePreviewTests
{
    private static RebasePreview Preview(int replayed = 1, int onto = 1, IReadOnlyList<GitPath>? bothSides = null, int published = 0, int listed = 0) =>
        new(Snapshots.HeadId, Snapshots.Id('b'), [.. Enumerable.Range(0, listed).Select(index => Snapshots.Commit(Snapshots.Id((char)('c' + index)), "Commit"))],
            replayed, onto, bothSides ?? [], "origin/main", published);

    [Fact]
    public void WhenTheBranchHasNothingToReplayAndTheOntoBranchOneCommitMore_ShouldOnlyMoveIt() =>
        Assert.True(Preview(replayed: 0, onto: 1).OnlyMoves);

    [Fact]
    public void WhenOneFileChangedOnBothSides_ShouldSaySo() =>
        Assert.True(Preview(bothSides: [Snapshots.Path("a.txt")]).HasBothSides);

    [Fact]
    public void WhenTheUpstreamHasOneOfTheBranchsCommits_ShouldCallTheBranchPublished() =>
        Assert.True(Preview(published: 1).IsPublished);

    [Fact]
    public void WhenEveryReplayedCommitIsListed_ShouldCountNoneMore() =>
        Assert.Equal(0, Preview(replayed: 2, listed: 2).MoreCount);
}
