using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.State;

// A repository whose refs Git keeps in reftable tables keeps ORIG_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD and REBASE_HEAD
// there too; only MERGE_HEAD stays a file. An operation stopped on a conflict reads as it does with files.
public sealed class ReftableStateTests
{
    // main and feature change conflict.txt, in a repository migrated to reftable before its first commit.
    private static async Task<GitFixture> ReftableAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("refs", "migrate", "--ref-format=reftable");
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("conflict.txt", "base\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("conflict.txt", "feature\n"); await f.CommitAsync("Feature");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("conflict.txt", "main\n"); await f.CommitAsync("Main");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    [Fact]
    public async Task WhenAMergeStopsOnConflicts_ShouldReadASupportedMergeAndFinishIt()
    {
        using var f = await ReftableAsync();
        var main = await f.GitAsync("rev-parse", "HEAD");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-edit", "feature")).ExitCode);
        await f.WriteAsync("conflict.txt", "resolved\n"); await f.GitAsync("add", "conflict.txt");
        var snapshot = await f.ReadAsync();

        var finish = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(snapshot, "Merge feature", FinishesMerge: true), f.Token);

        Assert.Equal(RepositoryOperation.Merge, snapshot.Operation);
        Assert.Equal(main, snapshot.Merge?.OriginalHead);
        Assert.True(snapshot.HasSupportedMerge, snapshot.Merge?.Limitation);
        Assert.True(finish.Outcome == GitWriteOutcome.Succeeded, finish.Message + finish.Details);
        Assert.Equal([main, await f.GitAsync("rev-parse", "feature")], (await f.GitAsync("show", "-s", "--format=%P", "HEAD")).Split(' '));
    }

    [Fact]
    public async Task WhenAMergeStopsOnConflicts_ShouldAbortIt()
    {
        using var f = await ReftableAsync();
        var main = await f.GitAsync("rev-parse", "HEAD");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-edit", "feature")).ExitCode);
        var snapshot = await f.ReadAsync();

        var abort = await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeAbortRequest(snapshot), f.Token);

        Assert.True(abort.Outcome == GitWriteOutcome.Succeeded, abort.Message + abort.Details);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.None, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenACherryPickStopsOnConflicts_ShouldReadThePickInProgress()
    {
        using var f = await ReftableAsync();
        var feature = await f.GitAsync("rev-parse", "feature");
        Assert.NotEqual(0, (await f.RunAsync("cherry-pick", "feature")).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.Equal(RepositoryOperation.CherryPick, snapshot.Operation);
        Assert.Equal(feature, snapshot.PickHead);
    }

    [Fact]
    public async Task WhenARevertStopsOnConflicts_ShouldReadTheRevertInProgress()
    {
        using var f = await ReftableAsync();
        var reverted = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync("conflict.txt", "later\n"); await f.CommitAsync("Later");
        Assert.NotEqual(0, (await f.RunAsync("revert", "--no-edit", reverted)).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.Equal(RepositoryOperation.Revert, snapshot.Operation);
        Assert.Equal(reverted, snapshot.PickHead);
    }

    [Fact]
    public async Task WhenARebaseStopsOnConflicts_ShouldNameTheStoppedCommit()
    {
        using var f = await ReftableAsync();
        var feature = await f.GitAsync("rev-parse", "feature");
        await f.GitAsync("switch", "feature");
        Assert.NotEqual(0, (await f.RunAsync("rebase", "main")).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.Equal(RepositoryOperation.Rebase, snapshot.Operation);
        Assert.Equal(feature, snapshot.Rebase?.Stopped);
    }

    // Git looks a bare name up in refs/tags and refs/heads when there is no root ref of that name, so a tag or a branch
    // named like one is no operation in progress.
    [Theory]
    [InlineData("tag", "CHERRY_PICK_HEAD")]
    [InlineData("branch", "REVERT_HEAD")]
    public async Task WhenATagOrBranchHasTheNameOfAPickHead_ShouldReadNoOperation(string command, string name)
    {
        using var f = await ReftableAsync();
        await f.GitAsync(command, name);

        var snapshot = await f.ReadAsync();

        Assert.Equal((RepositoryOperation.None, null), (snapshot.Operation, snapshot.PickHead));
    }

    [Fact]
    public async Task WhenATagHasTheNameOfTheMergeAutostash_ShouldReadAMergeWithoutAnAutostashAsSupported()
    {
        using var f = await ReftableAsync();
        await f.GitAsync("tag", "MERGE_AUTOSTASH");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-edit", "feature")).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.True(snapshot.HasSupportedMerge, snapshot.Merge?.Limitation);
    }

    // A merge Git started with --autostash keeps MERGE_AUTOSTASH in the tables, and Trag leaves that merge to Git.
    [Fact]
    public async Task WhenAMergeWithAnAutostashStopsOnConflicts_ShouldReadTheAutostashAsItsLimitation()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("conflict.txt", "dirty\n");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--autostash", "--no-edit", "feature")).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.Equal((RepositoryOperation.Merge, MergeText.MergeAutostash), (snapshot.Operation, snapshot.Merge?.Limitation));
    }
}
