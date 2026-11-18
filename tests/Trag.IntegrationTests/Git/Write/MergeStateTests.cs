using static Trag.IntegrationTests.Git.Write.MergeScenario;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.IntegrationTests.Git.Sync;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Write;

// Which merge states Trag takes over and which it leaves to Git: a merge started outside, foreign locks and changed
// metadata, and every state (rebase, cherry-pick, revert, sequencer, autostash, octopus) that blocks the merge writes.
public sealed class MergeStateTests
{
    // Pinned: a merge whose ORIG_HEAD is not HEAD, as a tool that does not write ORIG_HEAD leaves
    // it, is shown as one Trag does not finish or abort.
    [Fact]
    public async Task WhenOrigHeadDiffersFromHead_ShouldShowTheMergeAsUnsupportedAndRefuseFinishAndAbort()
    {
        using var f = await ConflictedInTheTerminalAsync();
        await f.GitAsync("update-ref", "ORIG_HEAD", await f.GitAsync("rev-parse", "HEAD~1"));
        var snapshot = await f.ReadAsync();
        var router = new GitWriteRouter(f.Runner);

        var finish = await router.ExecuteAsync(new CommitRequest(snapshot, "Merge", FinishesMerge: true), f.Token);
        var abort = await router.ExecuteAsync(new MergeAbortRequest(snapshot), f.Token);

        Assert.Equal(RepositoryOperation.Merge, snapshot.Operation); Assert.False(snapshot.HasSupportedMerge);
        Assert.Equal((GitWriteOutcome.Rejected, GitWriteOutcome.Rejected), (finish.Outcome, abort.Outcome));
        Assert.True(File.Exists(Path.Combine(f.Root, ".git", "MERGE_HEAD")));
    }

    // git merge --squash stages the branch's changes and starts no merge: Trag lists them as staged files, and a commit
    // made from them is an ordinary one.
    [Fact]
    public async Task WhenASquashMergeWasLeftInTheTerminal_ShouldShowStagedFilesWithoutAMergeAndCommitThem()
    {
        using var f = await Trag.TestSupport.GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("feature.txt", "feature\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.GitAsync("merge", "--squash", "feature");
        var snapshot = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(snapshot, "Squashed feature"), f.Token);

        Assert.Equal(RepositoryOperation.None, snapshot.Operation);
        Assert.Equal(["feature.txt"], snapshot.Files.Where(file => file.IsStaged).Select(file => file.Path.Display));
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Single((await f.GitAsync("show", "-s", "--format=%P", "HEAD")).Split(' '));
    }

    // main and feature change conflict.txt; the merge is started with Git's own command.
    private static async Task<Trag.TestSupport.GitFixture> ConflictedInTheTerminalAsync()
    {
        var f = await Trag.TestSupport.GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("conflict.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("conflict.txt", "feature\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("conflict.txt", "main\n"); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        return f;
    }

    [Fact]
    public async Task WhenAMergeStartedInTheTerminalMeetsAForeignLockOrChangedMetadata_ShouldTakeTheMergeOverButOverwriteNeither()
    {
        using var f = await SyncFixture.Create(); await Diverge(f); await f.Git(f.One, "fetch");
        var merge = await new GitProcessRunner().RunAsync(f.One, ["merge", "--no-ff", "--no-edit", "origin/trunk"], f.Token); Assert.Equal(1, merge.ExitCode);
        var expected = await f.Reader.ReadAsync(f.One, f.Token); Assert.True(expected.HasSupportedMerge);
        var path = Path.Combine(f.One, ".git", "index.lock"); await File.WriteAllTextAsync(path, "foreign lock", f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, (await AbortMerge(f)).Outcome);
        Assert.Equal("foreign lock", await File.ReadAllTextAsync(path, f.Token)); File.Delete(path);
        await File.AppendAllTextAsync(Path.Combine(f.One, ".git", "MERGE_MSG"), "changed externally\n", f.Token);
        var result = await new GitWriteRouter(new()).ExecuteAsync(new MergeAbortRequest(expected), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.True(File.Exists(Path.Combine(f.One, ".git", "MERGE_HEAD")));
    }

    // Git writes a large MERGE_MSG on its own: a merge with 1,200 conflicts lists each file under "# Conflicts:", and
    // merge.log puts a line per merged commit into the message of a merge left open with --no-commit. Neither makes the
    // merge anything but ordinary, and the repository must still open while it waits.
    [Theory]
    [InlineData("conflicts")]
    [InlineData("log")]
    public async Task WhenGitWritesAMergeMessageOverSixtyFourKilobytes_ShouldStillReadTheRepositoryAndItsMerge(string story)
    {
        using var f = await Trag.TestSupport.GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        var names = Enumerable.Range(0, 1200).Select(i => $"src/components/feature-module-{i:0000}/component-implementation.ts").ToArray();
        foreach (var name in names) await f.WriteAsync(name, "base\n");
        await f.CommitAsync("Base");
        await f.GitAsync("switch", "-q", "-c", "feature");
        if (story == "conflicts")
        {
            foreach (var name in names) await f.WriteAsync(name, "feature\n");
            await f.CommitAsync("Feature changes every component");
            await f.GitAsync("switch", "-q", "main");
            foreach (var name in names) await f.WriteAsync(name, "main\n");
            await f.CommitAsync("Main changes every component");
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        }
        else
        {
            var commits = string.Concat(Enumerable.Range(0, 1200).Select(i =>
                $"commit refs/heads/feature\ncommitter Fixture <fixture@example.invalid> {1700000000 + i} +0000\ndata <<END\nFeature step {i:0000}: adjust the component implementation and its tests\nEND\n"
                + (i == 0 ? "from refs/heads/feature^0\n" : "") + "\n"));
            await f.Runner.RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: System.Text.Encoding.UTF8.GetBytes(commits));
            await f.GitAsync("switch", "-q", "main");
            await f.GitAsync("commit", "--allow-empty", "-m", "Main moves on");
            Assert.Equal(0, (await f.RunAsync("merge", "--no-ff", "--no-commit", "--log=2000", "feature")).ExitCode);
        }
        Assert.True(new FileInfo(Path.Combine(f.Root, ".git", "MERGE_MSG")).Length > 64 * 1024);

        var snapshot = await f.ReadAsync();

        Assert.Equal(RepositoryOperation.Merge, snapshot.Operation);
        Assert.True(snapshot.HasSupportedMerge);
    }

    // A merge message in another encoding does not make the repository unreadable while the merge waits.
    [Fact]
    public async Task WhenTheMergeMessageIsNotUtf8_ShouldStillReadTheMerge()
    {
        using var f = await Conflict();
        await File.WriteAllBytesAsync(Path.Combine(f.One, ".git", "MERGE_MSG"), System.Text.Encoding.Latin1.GetBytes("Merge caf\u00e9\n"), f.Token);

        var merge = (await f.Reader.ReadAsync(f.One, f.Token)).Merge!;

        Assert.Equal("Merge caf\ufffd", merge.Message);
    }

    [Theory]
    [InlineData("rebase-merge")]
    [InlineData("CHERRY_PICK_HEAD")]
    [InlineData("REVERT_HEAD")]
    [InlineData("sequencer")]
    public async Task WhenAnotherOperationIsInProgress_ShouldBlockTheMergeWritesAndOrdinaryCommits(string marker)
    {
        using var f = await Conflict();
        var path = Path.Combine(f.One, ".git", marker);
        if (marker is "rebase-merge" or "sequencer") Directory.CreateDirectory(path); else await File.WriteAllTextAsync(path, await f.Head(f.One), f.Token);
        // Sequencer is lower priority than MERGE_HEAD; remove MERGE_HEAD in this fixture.
        if (marker == "sequencer") File.Delete(Path.Combine(f.One, ".git", "MERGE_HEAD"));
        Assert.False((await f.Reader.ReadAsync(f.One, f.Token)).HasSupportedMerge);
        // Nothing that belongs to a merge, and no ordinary commit either, may run while the merge state is broken.
        foreach (var build in new Func<RepositorySnapshot, GitWriteRequest>[] { expected => new MergeAbortRequest(expected),
            expected => new CommitRequest(expected, "blocked", FinishesMerge: true), expected => new CommitRequest(expected, "blocked") })
            Assert.Equal(GitWriteOutcome.Rejected, (await MergeScenario.Write(f, build)).Outcome);
    }

    [Fact]
    public async Task WhenTheMergeHasAnAutostashOrSeveralMergeHeads_ShouldLeaveItToGitAndRefuseToAbort()
    {
        using var f = await Conflict(); var git = Path.Combine(f.One, ".git"); var head = await f.Head(f.One);
        await File.WriteAllTextAsync(Path.Combine(git, "MERGE_AUTOSTASH"), head, f.Token);
        Assert.False((await f.Reader.ReadAsync(f.One, f.Token)).HasSupportedMerge);
        Assert.Equal(GitWriteOutcome.Rejected, (await AbortMerge(f)).Outcome);
        File.Delete(Path.Combine(git, "MERGE_AUTOSTASH")); await File.AppendAllTextAsync(Path.Combine(git, "MERGE_HEAD"), head + "\n", f.Token);
        Assert.False((await f.Reader.ReadAsync(f.One, f.Token)).HasSupportedMerge);
        Assert.Equal(GitWriteOutcome.Rejected, (await AbortMerge(f)).Outcome);
    }
}
