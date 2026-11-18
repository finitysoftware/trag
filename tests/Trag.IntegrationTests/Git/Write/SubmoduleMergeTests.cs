using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A merge that brings in another commit of a submodule, and one where both branches moved it. The merge records the commit
// and leaves the submodule's checkout to Update submodules; a conflict on the submodule is resolved by choosing the commit
// one side records.
public sealed class SubmoduleMergeTests
{
    // feature moves sub to a commit of its own. Back on main, sub is checked out at "Sub one" again, and main either
    // changes a.txt or moves sub to another commit as well. Returns main's commit of sub and feature's.
    private static async Task<(string Mine, string Theirs)> BranchesAsync(GitFixture f, bool mainMoves)
    {
        var sub = Path.Combine(f.Root, "sub");
        var one = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.GitAsync("switch", "-c", "feature");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub feature");
        await f.GitAsync("add", "sub");
        await f.GitAsync("commit", "-m", "Feature moves sub");
        var theirs = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.GitAsync("switch", "main");
        await f.GitAtAsync(sub, "checkout", "-q", one);
        if (!mainMoves)
        {
            await f.WriteAsync("a.txt", "main\n");
            await f.CommitAsync("Main", "a.txt");
            return (one, theirs);
        }
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub main");
        await f.GitAsync("add", "sub");
        await f.GitAsync("commit", "-m", "Main moves sub");
        return (await f.GitAtAsync(sub, "rev-parse", "HEAD"), theirs);
    }

    private static async Task<GitWriteResult> MergeAsync(GitFixture f) => await new GitWriteRouter(f.Runner).ExecuteAsync(
        new MergeRequest(await f.ReadAsync(), "feature", await f.GitAsync("rev-parse", "feature"), false, "Merge branch 'feature'"), f.Token);

    [Fact]
    public async Task WhenTheIncomingBranchMovedASubmodule_ShouldMergeItAndLeaveTheCheckoutToTheUpdate()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var (mine, theirs) = await BranchesAsync(f, mainMoves: false);

        var merged = await MergeAsync(f);

        Assert.True(merged.Outcome == GitWriteOutcome.Succeeded, merged.Message + merged.Details);
        Assert.Equal(theirs, await f.GitAsync("rev-parse", "HEAD:sub"));
        Assert.Equal(mine, await f.GitAtAsync(Path.Combine(f.Root, "sub"), "rev-parse", "HEAD"));
        Assert.Equal(["sub"], (await f.ReadAsync()).SubmodulesToUpdate);
    }

    // The incoming branch removes the submodule: the merge takes it out of the index and leaves its folder, which holds
    // a repository of its own, where it is.
    [Fact]
    public async Task WhenTheIncomingBranchRemovesASubmodule_ShouldMergeAndLeaveTheFolder()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.GitAsync("switch", "-c", "feature");
        await f.GitAsync("rm", "--cached", "--quiet", "sub");
        await f.GitAsync("config", "-f", ".gitmodules", "--remove-section", "submodule.sub");
        await f.GitAsync("add", ".gitmodules");
        await f.GitAsync("commit", "-m", "Remove sub");
        await f.GitAsync("switch", "main");

        var merged = await MergeAsync(f);

        Assert.True(merged.Outcome == GitWriteOutcome.Succeeded, merged.Message + merged.Details);
        Assert.NotEqual(0, (await f.RunAsync("rev-parse", "--verify", "--quiet", "HEAD:sub")).ExitCode);
        Assert.True(Directory.Exists(Path.Combine(f.Root, "sub", ".git")) || File.Exists(Path.Combine(f.Root, "sub", ".git")));
    }

    // Files of the submodule's own and its checkout at another commit are its own repository's; the merge goes ahead.
    [Fact]
    public async Task WhenTheSubmoduleHasChangesOfItsOwn_ShouldStillMerge()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await BranchesAsync(f, mainMoves: false);
        await f.WriteAsync(Path.Combine("sub", "build.log"), "built\n");

        var merged = await MergeAsync(f);

        Assert.True(merged.Outcome == GitWriteOutcome.Succeeded, merged.Message + merged.Details);
    }

    // A submodule's side is a commit, not text: the merge view says so, and the chosen side's commit is what the index
    // records, whatever the submodule has checked out.
    [Theory]
    [InlineData(ConflictChoice.Mine)]
    [InlineData(ConflictChoice.Theirs)]
    public async Task WhenBothBranchesMovedASubmodule_ShouldResolveTheConflictWithTheChosenSidesCommit(ConflictChoice side)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var (mine, theirs) = await BranchesAsync(f, mainMoves: true);
        await MergeAsync(f);
        var conflicted = await f.ReadAsync();
        var file = Assert.Single(conflicted.Files, file => file.IsConflict);
        var shown = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, file, f.Token);

        var resolved = await new GitWriteRouter(f.Runner).ExecuteAsync(new ConflictSideRequest(conflicted, file.Path, side), f.Token);

        Assert.Equal(MergeText.SubmoduleConflict, shown.Message);
        Assert.True(resolved.Outcome == GitWriteOutcome.Succeeded, resolved.Message + resolved.Details);
        Assert.Equal("160000 " + (side == ConflictChoice.Mine ? mine : theirs) + " 0\tsub", await f.GitAsync("ls-files", "--stage", "--", "sub"));
        Assert.Equal(side == ConflictChoice.Mine ? [] : ["sub"], (await f.ReadAsync()).SubmodulesToUpdate);
    }
}
