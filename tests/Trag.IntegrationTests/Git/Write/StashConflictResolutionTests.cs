using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Conflicts a stash left when it was applied over changed files, resolved with real Git through the same choices a merge
// offers: a side for a whole file, one block at a time, and staging the result. Nothing is finished afterwards: with
// the last conflict staged the repository is writable again.
public sealed class StashConflictResolutionTests
{
    // main changes f.txt and g.txt after both were changed and stashed; the stash is applied over them.
    private static async Task<GitFixture> Applied()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.WriteAsync("g.txt", "x\n"); await f.CommitAsync("Base");
            await f.WriteAsync("f.txt", "a\nB-stash\nc\n"); await f.WriteAsync("g.txt", "x-stash\n"); await f.GitAsync("stash");
            await f.WriteAsync("f.txt", "a\nB-main\nc\n"); await f.WriteAsync("g.txt", "x-main\n"); await f.CommitAsync("Main");
            Assert.NotEqual(0, (await f.RunAsync("stash", "apply")).ExitCode);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitWriteResult> Write(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);
    private static string Read(GitFixture f, string path) => File.ReadAllText(Path.Combine(f.Root, path));

    // Each open conflict has its blocks counted like a merge's, though Git keeps no commit to tell the stash's side by.
    [Fact]
    public async Task WhenAStashLeftConflicts_ShouldCountTheBlocksOfEachOpenOne()
    {
        using var f = await Applied();

        var open = (await f.ReadAsync()).Resolutions.Open;

        Assert.Equal([("f.txt", (int?)1), ("g.txt", 1)], open.Select(conflict => (conflict.Path.Display, conflict.Blocks)).OrderBy(pair => pair.Display));
    }

    [Fact]
    public async Task WhenAStashLeftConflicts_ShouldResolveThemWithASideOrABlockAndLeaveTheRepositoryWritable()
    {
        using var f = await Applied();
        var before = await f.ReadAsync();
        Assert.Equal(RepositoryOperation.Conflict, before.Operation);

        var stash = await Write(f, new ConflictSideRequest(before, Snapshots.Path("g.txt"), ConflictChoice.Theirs));
        Assert.True(stash.Outcome == GitWriteOutcome.Succeeded, stash.Message + stash.Details);
        Assert.Equal("x-stash\n", Read(f, "g.txt"));

        var shown = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, shown.Conflicts.Single(), f.Token);
        var both = await Write(f, new ConflictBlockRequest(shown, Snapshots.Path("f.txt"), 0, ConflictChoice.Both, text.Hash));
        Assert.True(both.Outcome == GitWriteOutcome.Succeeded, both.Message + both.Details);
        var staged = await Write(f, new StageRequest(await f.ReadAsync(), [Snapshots.Path("f.txt")], Resolution: true));
        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);

        var after = await f.ReadAsync();
        Assert.Empty(after.Conflicts);
        Assert.Equal(RepositoryOperation.None, after.Operation);
        Assert.False(after.HasUnsupportedWriteState);
        Assert.Equal("a\nB-main\nB-stash\nc\n", Read(f, "f.txt"));
    }
}
