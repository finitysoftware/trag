using Trag.Domain.Branches;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Branches;

public sealed class StartPointTests
{
    [Fact]
    public void WhenTheDialogOpensForACommit_ShouldOfferHeadThenTheCommitThenTheOtherLocalAndTheRemoteBranches()
    {
        var catalog = BranchCatalog.Create(new([new("main", "refs/heads/main", "1", false), new("topic", "refs/heads/topic", "2", false)],
            [new("origin/release", "refs/remotes/origin/release", "3", true, "origin")], "main"), Snapshots.Create());
        var commit = StartPoint.AtCommit(new HistoryCommit(new string('c', 40), "A", "a@example.invalid", "2026-09-24T10:00:00+00:00", "Fix it"));
        var points = StartPoint.For(catalog, "main", commit);
        Assert.Equal(["main · current HEAD", "cccccccccc · Fix it", "topic", "origin/release"], points.Select(point => point.Display));
        Assert.Equal([null, new string('c', 40), "refs/heads/topic", "refs/remotes/origin/release"], points.Select(point => point.Ref));
    }

    [Fact]
    public void WhenNoBranchIsReadAndNoCommitChosen_ShouldOfferHeadAlone()
    {
        Assert.Equal("HEAD · current HEAD", StartPoint.For(BranchCatalog.Empty, null, null).Single().Display);
    }
}
