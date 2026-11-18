using Trag.Domain.Rebase;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// The commits a rewrite from one commit of the current branch would take: that commit and every one after it, oldest
// first, with whole messages, after the commit before it; nothing for a commit the branch does not have.
public sealed class RewriteRangeTests
{
    [Fact]
    public async Task WhenACommitOfTheCurrentBranchIsChosen_ShouldListItAndTheCommitsAfterItOldestFirst()
    {
        using var f = await GitFixture.CreateAsync("main");
        var a = await f.CommitAsync("A");
        var b = await f.CommitAsync("B\n\nBody of b");
        var c = await f.CommitAsync("C");

        var range = await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, b, f.Token);

        Assert.Equal((a, 0, false), (range!.Base, range.Merges, range.StartsAtShallowBoundary));
        Assert.Equal([(b, "B", "B\n\nBody of b"), (c, "C", "C")], range.Steps.Select(step => (step.Id, step.Subject, step.Original)));
        Assert.All(range.Steps, step => Assert.Equal((RewriteAction.Pick, step.Original), (step.Action, step.Message)));
    }

    [Fact]
    public async Task WhenTheCommitIsNotOnTheCurrentBranch_ShouldListNothing()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("A");
        await f.GitAsync("switch", "-c", "side");
        var side = await f.CommitAsync("Side");
        await f.GitAsync("switch", "main");

        Assert.Null(await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, side, f.Token));
    }

    // The first commit has nothing before it: the whole history goes, from the root. It names no parent either, so it is no
    // shallow clone's boundary.
    [Fact]
    public async Task WhenTheFirstCommitIsChosen_ShouldListTheWholeHistoryWithNoBase()
    {
        using var f = await GitFixture.CreateAsync("main");
        var a = await f.CommitAsync("A");
        var b = await f.CommitAsync("B");

        var range = await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, a, f.Token);

        Assert.Null(range!.Base);
        Assert.False(range.StartsAtShallowBoundary);
        Assert.Equal([a, b], range.Steps.Select(step => step.Id));
    }

    [Fact]
    public async Task WhenTheCommitsAfterItHoldAMerge_ShouldCountIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("A");
        var b = await f.CommitAsync("B");
        await f.GitAsync("switch", "-c", "side");
        await f.CommitAsync("Side");
        await f.GitAsync("switch", "main");
        await f.CommitAsync("C");
        await f.GitAsync("merge", "--no-ff", "-m", "Merge side", "side");

        var range = await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, b, f.Token);

        Assert.Equal(1, range!.Merges);
    }

    // Git reads a lone HEAD as a revision or as a path, and refuses when a file of that name is there (on Windows in any
    // case): from the first commit the range is HEAD alone.
    [Fact]
    public async Task WhenATopLevelFileIsNamedHead_ShouldStillCountMerges()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("HEAD", "a file\n");
        var a = await f.CommitAsync("A");
        await f.GitAsync("switch", "-c", "side");
        await f.CommitAsync("Side");
        await f.GitAsync("switch", "main");
        await f.CommitAsync("C");
        await f.GitAsync("merge", "--no-ff", "-m", "Merge side", "side");

        var range = await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, a, f.Token);

        Assert.Equal((1, a), (range!.Merges, range.Steps[0].Id));
    }
}
