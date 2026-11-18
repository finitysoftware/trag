using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// main goes back from its third commit to its first; each mode leaves the index and the files its own way, and the
// branch stays checked out.
public sealed class ResetTests
{
    [Fact]
    public async Task WhenTheBranchIsResetSoft_ShouldMoveItBackAndKeepTheLeavingChangesStaged()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);

        var result = await ResetAsync(fixture, first, ResetMode.Soft);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(ResetText.Done("main", GitObjectId.Short(first), ResetMode.Soft), result.Message);
        Assert.Equal((first, "main"), (await fixture.GitAsync("rev-parse", "HEAD"), await fixture.GitAsync("branch", "--show-current")));
        Assert.Equal("one.txt\ntwo.txt", await fixture.GitAsync("diff", "--cached", "--name-only"));
        Assert.Equal("2\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "one.txt")));
    }

    [Fact]
    public async Task WhenTheBranchIsResetMixed_ShouldMoveItBackAndLeaveTheChangesUnstaged()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);

        var result = await ResetAsync(fixture, first, ResetMode.Mixed);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(first, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("", await fixture.GitAsync("diff", "--cached", "--name-only"));
        Assert.Equal("2\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "one.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "two.txt")));
    }

    // An uncommitted change goes with the commits; an untracked file stays.
    [Fact]
    public async Task WhenTheBranchIsResetHard_ShouldMakeTheTrackedFilesTheCommitsAndKeepUntrackedOnes()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        await fixture.WriteAsync("one.txt", "dirty\n"); await fixture.WriteAsync("scratch.txt", "keep\n");

        var result = await ResetAsync(fixture, first, ResetMode.Hard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(first, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("1", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "one.txt"))).TrimEnd());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "two.txt")));
        Assert.Equal("keep\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "scratch.txt")));
    }

    // Another program holds the index: Git refuses, and the branch stays where it was.
    [Fact]
    public async Task WhenGitCannotWriteTheIndex_ShouldRefuseAndLeaveTheBranchWhereItWas()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        var third = await fixture.GitAsync("rev-parse", "HEAD");
        var expected = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "index.lock"), "");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new ResetRequest(expected, first, ResetMode.Hard), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, ResetText.Failed), (result.Outcome, result.Message));
        Assert.Equal(third, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // A merge waiting to be finished or aborted is not a branch to move back: the reset is refused before Git runs.
    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldRefuseTheResetWithStateUnsupported()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        await fixture.GitAsync("switch", "-c", "side", first);
        await fixture.WriteAsync("one.txt", "side\n"); await fixture.CommitAsync("side", "one.txt");
        await fixture.GitAsync("switch", "main");
        Assert.NotEqual(0, (await fixture.RunAsync("merge", "--no-ff", "--no-edit", "side")).ExitCode);
        var head = await fixture.GitAsync("rev-parse", "HEAD");

        var result = await ResetAsync(fixture, first, ResetMode.Hard);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.StateUnsupported), (result.Outcome, result.Message));
        Assert.Equal(head, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".git", "MERGE_HEAD")));
    }

    // The question was asked about the branch as it was shown; a commit made outside Trag since is a branch other than
    // the one shown.
    [Fact]
    public async Task WhenHeadMovedAfterTheSnapshot_ShouldRefuseAsChangedOutside()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        var shown = await fixture.ReadAsync();
        await fixture.WriteAsync("three.txt", "three\n");
        var outside = await fixture.CommitAsync("made outside", "three.txt");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new ResetRequest(shown, first, ResetMode.Mixed), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.ChangedOutside), (result.Outcome, result.Message));
        Assert.Equal(outside, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // A hard reset rewrites tracked files only: untracked and ignored files are not Git's to take.
    [Fact]
    public async Task WhenUntrackedAndIgnoredFilesExist_ShouldKeepThemOnAHardReset()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync(".gitignore", "*.log\n"); await fixture.CommitAsync("ignore logs", ".gitignore");
        var first = await ThreeCommitsAsync(fixture);
        await fixture.WriteAsync("notes.txt", "mine\n");
        await fixture.WriteAsync("build.log", "log\n");

        var result = await ResetAsync(fixture, first, ResetMode.Hard);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("mine\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "notes.txt"), fixture.Token));
        Assert.Equal("log\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "build.log"), fixture.Token));
    }

    // What a hard reset writes where only untracked files can be: the files the commit tracks that the index does not,
    // named as Git has them.
    [Fact]
    public async Task WhenTheCommitTracksFilesTheIndexDoesNot_ShouldReadThemForTheHardResetQuestion()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("one.txt", "1\n"); await fixture.WriteAsync(Path.Combine("gone", "a č.txt"), "a\n");
        var first = await fixture.CommitAsync("first");
        await fixture.GitAsync("rm", "--quiet", "-r", "gone"); await fixture.CommitAsync("second");

        var notInIndex = await new CliBranchReader(fixture.Runner).ReadNotInIndexAsync(fixture.Root, first, fixture.Token);

        Assert.Equal(["gone/a č.txt"], notInIndex.Select(path => path.Display));
    }

    // A commit merged in from a side branch is on the branch through the merge's second parent alone: every commit after
    // it leaves, the merge and the first-parent ones included.
    [Fact]
    public async Task WhenTheCommitIsReachableOnlyThroughASecondParent_ShouldCountAllLeavingCommitsAndReset()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        await fixture.GitAsync("switch", "-c", "side", first);
        await fixture.WriteAsync("side.txt", "side\n"); var side = await fixture.CommitAsync("side", "side.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.GitAsync("merge", "--no-ff", "--no-edit", "side");

        var distance = await new CliBranchReader(fixture.Runner).DistanceAsync(fixture.Root, side, fixture.Token);
        var result = await ResetAsync(fixture, side, ResetMode.Hard);

        Assert.Equal(3, distance); // second, third and the merge
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(side, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // Windows: a file another program holds open without sharing cannot be rewritten by a hard reset. Whatever Git
    // managed, the outcome never claims a reset that did not happen.
    [Fact]
    public async Task WhenAHardResetCannotRewriteALockedFile_ShouldNotClaimASuccessfulReset()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync("main");
        var first = await ThreeCommitsAsync(fixture);
        GitWriteResult result;
        using (new FileStream(Path.Combine(fixture.Root, "one.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await ResetAsync(fixture, first, ResetMode.Hard);

        var head = await fixture.GitAsync("rev-parse", "HEAD");
        var rewritten = (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "one.txt"), fixture.Token)).Replace("\r\n", "\n") == "1\n";
        Assert.True(result.Outcome != GitWriteOutcome.Succeeded || head == first && rewritten, result.Outcome + ": " + result.Message + " " + result.Details);
    }

    // With submodule.recurse=true Git's hard reset also resets each submodule's working tree and detaches its HEAD. The
    // question names nothing inside a submodule, so the work there stays: the submodule's edit, and its branch checked out.
    [Fact]
    public async Task WhenSubmoduleRecurseIsOn_ShouldHardResetWithoutLosingSubmoduleWork()
    {
        using var fixture = await SubmoduleFixture.CreateAsync();
        var added = await fixture.GitAsync("rev-parse", "HEAD");
        await fixture.WriteAsync("b.txt", "b\n"); await fixture.CommitAsync("Later", "b.txt");
        var sub = Path.Combine(fixture.Root, "sub");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "committed\n", fixture.Token);
        await fixture.GitAtAsync(sub, "add", "s.txt"); await fixture.GitAtAsync(sub, "commit", "-m", "Sub two");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "uncommitted work\n", fixture.Token);
        await fixture.GitAsync("config", "submodule.recurse", "true");

        var result = await ResetAsync(fixture, added, ResetMode.Hard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(added, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("uncommitted work\n", await File.ReadAllTextAsync(Path.Combine(sub, "s.txt"), fixture.Token));
        Assert.Equal("main", await fixture.GitAtAsync(sub, "branch", "--show-current"));
    }

    // one.txt "1", then one.txt "2", then two.txt; answers the first commit.
    private static async Task<string> ThreeCommitsAsync(GitFixture fixture)
    {
        await fixture.WriteAsync("one.txt", "1\n");
        var first = await fixture.CommitAsync("first", "one.txt");
        await fixture.WriteAsync("one.txt", "2\n"); await fixture.CommitAsync("second", "one.txt");
        await fixture.WriteAsync("two.txt", "two\n"); await fixture.CommitAsync("third", "two.txt");
        return first;
    }

    private static async Task<GitWriteResult> ResetAsync(GitFixture fixture, string commit, ResetMode mode) =>
        await new GitWriteRouter(fixture.Runner).ExecuteAsync(new ResetRequest(await fixture.ReadAsync(), commit, mode), fixture.Token);
}
