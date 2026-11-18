using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Discarding a submodule's row: a submodule moved to another commit goes back to the one the index records, without
// the network; changes inside the submodule are its own repository's, so they are discarded there, not from here.
public sealed class SubmoduleDiscardTests
{
    [Fact]
    public async Task WhenAMovedSubmoduleIsDiscarded_ShouldCheckOutTheRecordedCommit()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        var recorded = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var moved = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(moved, [moved.Files.Single().Path]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(recorded, await f.GitAtAsync(sub, "rev-parse", "HEAD"));
        Assert.Empty((await f.ReadAsync()).Files);
    }

    // A submodule checked out with its own submodules goes back with them: each returns to the commit its parent's
    // recorded commit records, so the submodule has no changes of its own afterwards.
    [Fact]
    public async Task WhenNestedSubmodulesMoved_ShouldRestoreThemAll()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        var innerOrigin = Path.Combine(f.Container, "inner-origin");
        Directory.CreateDirectory(innerOrigin);
        await f.GitAtAsync(innerOrigin, "init", "--initial-branch=main");
        await SubmoduleFixture.IdentifyAsync(f, innerOrigin);
        await f.GitAtAsync(innerOrigin, "commit", "--allow-empty", "-m", "Inner one");
        await f.GitAtAsync(sub, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(innerOrigin).AbsoluteUri, "inner");
        await f.GitAtAsync(sub, "commit", "-m", "Add inner");
        var before = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        var inner = Path.Combine(sub, "inner");
        await SubmoduleFixture.IdentifyAsync(f, inner);
        await f.GitAtAsync(inner, "commit", "--allow-empty", "-m", "Inner two");
        var recordedInner = await f.GitAtAsync(inner, "rev-parse", "HEAD");
        await f.GitAtAsync(sub, "commit", "-am", "Move inner");
        await f.GitAsync("commit", "-am", "Move sub");
        await f.GitAtAsync(sub, "checkout", "--recurse-submodules", "--detach", before);
        var moved = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(moved, [moved.Files.Single().Path]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(recordedInner, await f.GitAtAsync(inner, "rev-parse", "HEAD"));
        Assert.Empty((await f.ReadAsync()).Files);
    }

    // Discard stays local: a recorded commit the submodule does not have is not fetched for it, though the submodule's
    // own configuration would let Git fetch from its origin.
    [Fact]
    public async Task WhenTheSubmoduleLacksTheRecordedCommit_ShouldRefuseWithoutFetchingIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var origin = SubmoduleFixture.Origin(f);
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "config", "protocol.file.allow", "always");
        await f.GitAtAsync(origin, "commit", "--allow-empty", "-m", "Sub elsewhere");
        var elsewhere = await f.GitAtAsync(origin, "rev-parse", "HEAD");
        await f.GitAsync("update-index", "--cacheinfo", "160000," + elsewhere + ",sub");
        var moved = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(moved, [moved.Files.Single(file => file.IsSubmoduleMoved).Path]), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.NotEqual(0, (await f.RunAtAsync(Path.Combine(f.Root, "sub"), "cat-file", "-e", elsewhere + "^{commit}")).ExitCode);
    }

    // Changes inside a submodule, a modified file of its own or an untracked one, are its own repository's.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenASubmoduleWithChangesOfItsOwnIsDiscarded_ShouldRefuseAndLeaveThem(bool tracked)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        if (tracked)
        {
            await f.WriteAsync(Path.Combine("sub", "own.txt"), "own\n");
            await f.GitAtAsync(sub, "add", "own.txt"); await f.GitAtAsync(sub, "commit", "-m", "Own");
            await f.GitAsync("add", "sub"); await f.GitAsync("commit", "-m", "Move sub");
        }
        await f.WriteAsync(Path.Combine("sub", "own.txt"), "changed\n");
        var changed = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(changed, [changed.Files.Single().Path]), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.SubmoduleChangesKept("sub")), (result.Outcome, result.Message));
        Assert.Equal("changed\n", await File.ReadAllTextAsync(Path.Combine(sub, "own.txt"), f.Token));
    }
}
