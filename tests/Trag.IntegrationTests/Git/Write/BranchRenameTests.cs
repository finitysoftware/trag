using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A local branch renamed by real Git: the current one, which HEAD follows and whose upstream stays, or another one; a
// name another branch has, a name Git would not take, and a branch gone since the list was read are refused, and
// nothing changes then.
public sealed class BranchRenameTests
{
    private static async Task<string[]> BranchesAsync(SyncFixture f) =>
        (await f.Git(f.One, "branch", "--no-column", "--format=%(refname:short)")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task WhenTheCurrentBranchIsRenamed_ShouldStayOnItUnderTheNewNameWithItsUpstream()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);

        var result = await writer.ExecuteAsync(new BranchRenameRequest(await f.Reader.ReadAsync(f.One, f.Token), "trunk", "main"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(BranchOutcomeText.Renamed("trunk", "main"), result.Message);
        Assert.Equal("main", await f.Git(f.One, "branch", "--show-current"));
        Assert.Equal("origin/trunk", await f.Git(f.One, "rev-parse", "--abbrev-ref", "main@{upstream}"));
        Assert.Equal("Branch: renamed refs/heads/trunk to refs/heads/main", await f.Git(f.One, "reflog", "-1", "--format=%gs", "HEAD"));
    }

    [Fact]
    public async Task WhenAnotherBranchIsRenamed_ShouldRenameItAndLeaveTheCurrentOneAsItIs()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await f.Git(f.One, "branch", "topic");

        var result = await writer.ExecuteAsync(new BranchRenameRequest(await f.Reader.ReadAsync(f.One, f.Token), "topic", "feature/topic"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["feature/topic", "trunk"], await BranchesAsync(f));
        Assert.Equal("trunk", await f.Git(f.One, "branch", "--show-current"));
    }

    // A branch whose name starts with a dash, as a tool writing refs directly can leave, is renamed as a name: Git does not
    // take it for an option and rename the current branch instead.
    [Fact]
    public async Task WhenTheBranchNameLooksLikeAnOption_ShouldRenameThatBranch()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "update-ref", "refs/heads/-f", "HEAD");

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(new BranchRenameRequest(await f.Reader.ReadAsync(f.One, f.Token), "-f", "topic"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["topic", "trunk"], await BranchesAsync(f));
        Assert.Equal("trunk", await f.Git(f.One, "branch", "--show-current"));
    }

    [Fact]
    public async Task WhenTheNameIsTakenOrInvalidOrTheBranchIsGone_ShouldRefuseAndChangeNothing()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await f.Git(f.One, "branch", "topic");
        await f.Git(f.One, "branch", "other");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        foreach (var (name, newName, message) in new[] { ("topic", "other", BranchOutcomeText.NameTaken("other")), ("topic", "two words", BranchOutcomeText.NameInvalid),
                     ("missing", "found", BranchOutcomeText.Gone) })
        {
            var result = await writer.ExecuteAsync(new BranchRenameRequest(snapshot, name, newName), f.Token);
            Assert.Equal((GitWriteOutcome.Rejected, message), (result.Outcome, result.Message));
        }
        Assert.Equal(["other", "topic", "trunk"], await BranchesAsync(f));
    }

    // Git locks the HEAD of a worktree, this one or a linked one, only to move it off the renamed branch, so a lock of
    // either HEAD that another program takes while the rename of another branch is stopped belongs to that program and stays.
    [Fact]
    public async Task WhenTheRenameOfAnotherBranchIsCanceledWhileAnotherProgramLocksTheHeads_ShouldLeaveThoseLocks()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "topic");
        await f.GitAsync("worktree", "add", "-b", "other", Path.Combine(f.Container, "wt"));
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "reference-transaction"),
            "#!/bin/sh\nupdates=$(cat)\n[ \"$1\" = prepared ] || exit 0\ntouch .git/HEAD.lock .git/worktrees/wt/HEAD.lock\necho ready > .git/trag-ready\nsleep 30\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        using var cancellation = new CancellationTokenSource();

        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRenameRequest(await f.ReadAsync(), "topic", "renamed"), cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-ready")) && !pending.IsCompleted) await Task.Delay(20, f.Token);
        await cancellation.CancelAsync();
        var result = await pending;

        Assert.True(File.Exists(Path.Combine(f.Root, ".git", "HEAD.lock")), result.Outcome + " " + result.Message);
        Assert.True(File.Exists(Path.Combine(f.Root, ".git", "worktrees", "wt", "HEAD.lock")), result.Outcome + " " + result.Message);
    }

    // A lock another program holds on the HEAD of a worktree that has the branch lets Git rename the branch but not move
    // that HEAD, and Trag cannot move it either, so that worktree stays on a name that is gone.
    [Fact]
    public async Task WhenAnotherProgramHoldsTheHeadOfTheWorktreeThatHasTheBranch_ShouldSayTheStateIsUnexpected()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("worktree", "add", "-b", "topic", Path.Combine(f.Container, "wt"));
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "worktrees", "wt", "HEAD.lock"), "", f.Token);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRenameRequest(await f.ReadAsync(), "topic", "renamed"), f.Token);

        Assert.Equal((GitWriteOutcome.Uncertain, BranchOutcomeText.UnexpectedState), (result.Outcome, result.Message));
    }

    // Git moves the HEAD of a linked worktree that has the renamed branch checked out in a transaction of its own, which
    // locks that worktree's HEAD, or its own reftable stack. A Git stopped there has renamed the branch, so that HEAD goes
    // to the new name as Git would have moved it. Git 2.45 still moves that HEAD outside a transaction, 2.50 in one, and
    // git refs migrate comes with 2.46.
    [GitTheory(2, 50)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheRenameOfABranchAnotherWorktreeHasIsCanceledWhileGitMovesItsHead_ShouldMoveThatHeadAndLeaveNoLock(bool reftable)
    {
        using var f = await GitFixture.CreateAsync("main");
        if (reftable) await f.GitAsync("refs", "migrate", "--ref-format=reftable");
        await f.CommitAsync("one");
        await f.GitAsync("worktree", "add", "-b", "topic", Path.Combine(f.Container, "wt"));
        var hooks = Path.Combine(f.Container, "hooks");
        var ready = Path.Combine(f.Root, ".git", "trag-ready");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "reference-transaction"),
            "#!/bin/sh\nupdates=$(cat)\n[ \"$1\" = prepared ] || exit 0\ncase \"$updates\" in *\" HEAD\"*) ;; *) exit 0 ;; esac\n[ -e '" + ready.Replace('\\', '/') + "' ] && exit 0\n"
            + "echo ready > '" + ready.Replace('\\', '/') + "'\nsleep 30\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        using var cancellation = new CancellationTokenSource();

        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRenameRequest(await f.ReadAsync(), "topic", "renamed"), cancellation.Token);
        while (!File.Exists(ready) && !pending.IsCompleted) await Task.Delay(20, f.Token);
        await cancellation.CancelAsync();
        var result = await pending;

        Assert.True(File.Exists(ready), "Git did not move the worktree's HEAD: " + result.Message);
        Assert.Equal((GitWriteOutcome.Succeeded, BranchOutcomeText.Renamed("topic", "renamed")), (result.Outcome, result.Message));
        Assert.Equal("refs/heads/renamed", (await f.GitAtAsync(Path.Combine(f.Container, "wt"), "symbolic-ref", "HEAD")).Trim());
        var git = Path.Combine(f.Root, ".git");
        Assert.Empty(Directory.GetFiles(git, "*.lock", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(git, path)));
    }

    // Git moves the branch's section of the configuration, its upstream with it, after the refs and every HEAD, through a
    // lock of the whole file. A Git stopped there leaves that lock, which refuses every later change of the configuration,
    // and the section under the old name; the write removes the lock and moves the section as Git would have.
    [Fact]
    public async Task WhenTheRenameIsCanceledWhileGitRewritesTheConfiguration_ShouldLeaveNoLockAndKeepTheUpstream()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "--track", "topic", "main");
        await ConfigurationLock.LengthenAsync(f);

        var result = await ConfigurationLock.CancelWhileHeldAsync(f, new BranchRenameRequest(await f.ReadAsync(), "topic", "renamed"));

        Assert.Equal((GitWriteOutcome.Succeeded, BranchOutcomeText.Renamed("topic", "renamed")), (result.Outcome, result.Message));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "config.lock")));
        Assert.Equal("main", (await f.GitAsync("rev-parse", "--abbrev-ref", "renamed@{upstream}")).Trim());
    }

    // A configuration lock another program holds lets Git rename the branch but not its section, and Trag cannot move the
    // section either, so the upstream stays under a name no branch has.
    [Fact]
    public async Task WhenAnotherProgramHoldsTheConfigurationLock_ShouldSayTheStateIsUnexpected()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "--track", "topic", "main");
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "config.lock"), "", f.Token);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRenameRequest(await f.ReadAsync(), "topic", "renamed"), f.Token);

        Assert.Equal((GitWriteOutcome.Uncertain, BranchOutcomeText.UnexpectedState), (result.Outcome, result.Message));
    }
}
