using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

public sealed class BranchContainsTests
{
    // HEAD has its own commits and those it was built on; a commit of another branch it has not.
    [Fact]
    public async Task WhenACommitIsAskedAbout_ShouldSayWhetherTheCurrentBranchAlreadyHasIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var one = await fixture.CommitAsync("one");
        await fixture.GitAsync("switch", "-c", "side");
        var side = await fixture.CommitAsync("side");
        await fixture.GitAsync("switch", "main");
        var two = await fixture.CommitAsync("two");
        var reader = new CliBranchReader(fixture.Runner);

        Assert.True(await reader.ContainsAsync(fixture.Root, one, fixture.Token));
        Assert.True(await reader.ContainsAsync(fixture.Root, two, fixture.Token));
        Assert.False(await reader.ContainsAsync(fixture.Root, side, fixture.Token));
    }

    // How far HEAD is ahead of one of its commits: the commits a reset to it takes off the branch; none for a commit
    // HEAD does not have.
    [Fact]
    public async Task WhenTheDistanceToACommitIsAsked_ShouldCountTheCommitsAfterItOnTheCurrentBranch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var one = await fixture.CommitAsync("one");
        await fixture.GitAsync("switch", "-c", "side");
        var side = await fixture.CommitAsync("side");
        await fixture.GitAsync("switch", "main");
        var two = await fixture.CommitAsync("two");
        await fixture.CommitAsync("three");
        var reader = new CliBranchReader(fixture.Runner);

        Assert.Equal(2, await reader.DistanceAsync(fixture.Root, one, fixture.Token));
        Assert.Equal(1, await reader.DistanceAsync(fixture.Root, two, fixture.Token));
        Assert.Null(await reader.DistanceAsync(fixture.Root, side, fixture.Token));
    }
}
