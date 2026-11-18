using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Trag reads every path it hands Git literally, and Git passes that setting on to the hooks it runs. The commands Trag
// names no paths for must not hand it on: a hook's own Git then reads pathspec magic as the user's Git does, here ':/'
// for the whole working tree, which git stash and the tools built on it depend on.
public sealed class HookPathspecTests
{
    private const string Magic = "if git ls-files --error-unmatch -- ':/' >/dev/null 2>&1; then echo magic; else echo literal; fi > .git/trag-hook-pathspec\n";

    [Fact]
    public async Task WhenTragSwitchesBranches_ShouldLetThePostCheckoutHookUsePathspecMagic()
    {
        using var f = await TwoBranchesAsync();
        await Hook(f, "post-checkout");

        var result = await Write(f, snapshot => new BranchRequest(snapshot, new("feature"), BranchAction.Switch));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    [Fact]
    public async Task WhenTragChecksOutACommit_ShouldLetThePostCheckoutHookUsePathspecMagic()
    {
        using var f = await TwoBranchesAsync();
        await Hook(f, "post-checkout");
        var feature = await f.GitAsync("rev-parse", "feature");

        var result = await Write(f, snapshot => new BranchRequest(snapshot, new(feature), BranchAction.Checkout));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    [Fact]
    public async Task WhenTragMerges_ShouldLetThePostMergeHookUsePathspecMagic()
    {
        using var f = await TwoBranchesAsync();
        await Hook(f, "post-merge");
        var feature = await f.GitAsync("rev-parse", "feature");

        var result = await Write(f, snapshot => new MergeRequest(snapshot, "feature", feature, true, "Merge branch 'feature'"));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    [Fact]
    public async Task WhenTragRebases_ShouldLetThePostCommitHookUsePathspecMagic()
    {
        using var f = await TwoBranchesAsync();
        await f.WriteAsync("three.txt", "three\n");
        var onto = await f.CommitAsync("Three", "three.txt");
        await f.GitAsync("switch", "feature");
        await Hook(f, "post-commit");

        var result = await Write(f, snapshot => new RebaseRequest(snapshot, "fixture", onto));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    // A revert runs through the same command as a cherry-pick.
    [Fact]
    public async Task WhenTragCherryPicks_ShouldLetThePostCommitHookUsePathspecMagic()
    {
        using var f = await TwoBranchesAsync();
        await Hook(f, "post-commit");
        var feature = await f.GitAsync("rev-parse", "feature");

        var result = await Write(f, snapshot => new CherryPickRequest(snapshot, feature));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    // Finishing a pick after its conflict commits it again, and runs the commit's hooks again.
    [Fact]
    public async Task WhenTragFinishesAPickAfterItsConflict_ShouldLetThePostCommitHookUsePathspecMagic()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("f.txt", "a\n");
        await f.CommitAsync("A", "f.txt");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("f.txt", "b\n");
        var side = await f.CommitAsync("B", "f.txt");
        await f.GitAsync("switch", "fixture");
        await f.WriteAsync("f.txt", "c\n");
        await f.CommitAsync("C", "f.txt");
        Assert.NotEqual(0, (await f.RunAsync("cherry-pick", side)).ExitCode);
        await f.GitAsync("checkout", "--theirs", "--", "f.txt");
        await f.GitAsync("add", "f.txt");
        await Hook(f, "post-commit");

        var result = await Write(f, snapshot => new PickContinueRequest(snapshot));

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.Root, f.Token));
    }

    // Every transfer runs through one command line; a push is the one with a hook.
    [Fact]
    public async Task WhenTragPushes_ShouldLetThePrePushHookUsePathspecMagic()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "pushed");
        await f.Hook(f.One, "pre-push", Magic);

        var result = await f.Execute(SyncAction.Push);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("magic", await HookSawAsync(f.One, f.Token));
    }

    // one.txt on the fixture branch; feature adds two.txt on top of it.
    private static async Task<GitFixture> TwoBranchesAsync()
    {
        var f = await GitFixture.CreateAsync();
        await f.WriteAsync("one.txt", "one\n");
        await f.CommitAsync("One", "one.txt");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("two.txt", "two\n");
        await f.CommitAsync("Two", "two.txt");
        await f.GitAsync("switch", "fixture");
        return f;
    }

    private static async Task<GitWriteResult> Write(GitFixture f, Func<Trag.Domain.Status.RepositorySnapshot, GitWriteRequest> build) =>
        await new GitWriteRouter(f.Runner).ExecuteAsync(build(await f.ReadAsync()), f.Token);

    private static async Task<string> HookSawAsync(string root, CancellationToken ct) =>
        (await File.ReadAllTextAsync(Path.Combine(root, ".git", "trag-hook-pathspec"), ct)).Trim();

    private static async Task Hook(GitFixture f, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(f.Container, "empty-hooks")).FullName;
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, "#!/bin/sh\n" + Magic, new UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
