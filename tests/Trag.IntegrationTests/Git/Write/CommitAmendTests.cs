using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// An amend replaces the last commit with one that has its parents, the new message and whatever is staged on top, and
// is verified like any commit.
public sealed class CommitAmendTests
{
    [Fact]
    public async Task WhenTheLastCommitIsAmendedWithANewMessageAndAStagedFile_ShouldReplaceItOnItsOwnParent()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt"); await f.GitAsync("commit", "-m", "Initial");
        var initial = await Text(f, "rev-parse", "HEAD");
        await Put(f, "two.txt", "two\n"); await f.GitAsync("add", "two.txt"); await f.GitAsync("commit", "-m", "Secnod");
        var replaced = await Text(f, "rev-parse", "HEAD");
        await Put(f, "three.txt", "three\n"); await f.GitAsync("add", "three.txt");

        var result = await Amend(f, "Second\n\nWith its body");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Last commit amended.", result.Message);
        var head = await Text(f, "rev-parse", "HEAD");
        Assert.Equal(head, result.CommitId); Assert.NotEqual(replaced, head);
        Assert.Equal(initial, await Text(f, "rev-parse", "HEAD^"));
        Assert.Equal("Second\n\nWith its body", await Text(f, "log", "-1", "--format=%B"));
        Assert.Equal("two\n", await GitText(f, "show", "HEAD:two.txt")); Assert.Equal("three\n", await GitText(f, "show", "HEAD:three.txt"));
        Assert.DoesNotContain((await f.ReadAsync()).Files, file => file.IsStaged);
    }

    // Rewording alone: nothing staged, and the very first commit, which has no parent to keep.
    [Fact]
    public async Task WhenTheFirstCommitIsAmendedWithNothingStaged_ShouldOnlyRewordIt()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt"); await f.GitAsync("commit", "-m", "Intial");
        var tree = await Text(f, "rev-parse", "HEAD^{tree}");

        var result = await Amend(f, "Initial");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("1", await Text(f, "rev-list", "--count", "HEAD"));
        Assert.Equal("Initial", await Text(f, "log", "-1", "--format=%B"));
        Assert.Equal(tree, await Text(f, "rev-parse", "HEAD^{tree}"));
    }

    // A merge commit amended stays a merge: both parents are kept, and the amend is verified against both.
    [Fact]
    public async Task WhenTheLastCommitIsAMerge_ShouldKeepBothParents()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt"); await f.GitAsync("commit", "-m", "Initial");
        await f.GitAsync("switch", "-c", "side");
        await Put(f, "side.txt", "side\n"); await f.GitAsync("add", "side.txt"); await f.GitAsync("commit", "-m", "Side");
        await f.GitAsync("switch", "fixture");
        await Put(f, "main.txt", "main\n"); await f.GitAsync("add", "main.txt"); await f.GitAsync("commit", "-m", "Main");
        await f.GitAsync("merge", "--no-ff", "--no-edit", "side");
        var parents = await Text(f, "show", "-s", "--format=%P", "HEAD");

        var result = await Amend(f, "Merge side, reworded");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(parents, await Text(f, "show", "-s", "--format=%P", "HEAD"));
        Assert.Equal("Merge side, reworded", await Text(f, "log", "-1", "--format=%B"));
    }

    [Fact]
    public async Task WhenHeadIsDetached_ShouldAmendTheDetachedCommit()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt"); await f.GitAsync("commit", "-m", "Initial");
        await Put(f, "two.txt", "two\n"); await f.GitAsync("add", "two.txt"); await f.GitAsync("commit", "-m", "Detahced");
        var branch = await Text(f, "rev-parse", "fixture");
        await f.GitAsync("switch", "--detach", "HEAD");

        var result = await Amend(f, "Detached");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Detached", await Text(f, "log", "-1", "--format=%B"));
        Assert.Equal("HEAD", await Text(f, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(branch, await Text(f, "rev-parse", "fixture"));
    }

    // An amend keeps the author of the commit it replaces, as Git's own amend does, and stamps the user as its committer.
    [Fact]
    public async Task WhenAmendingSomeoneElsesCommit_ShouldKeepTheirAuthorship()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt");
        await f.GitAsync("commit", "-m", "Thier work", "--author", "Ana Anić <ana@example.invalid>", "--date", "2026-01-02T03:04:05+01:00");

        var result = await Amend(f, "Their work");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Ana Anić <ana@example.invalid> 2026-01-02T03:04:05+01:00\nTrag Fixture <fixture@example.invalid>",
            await Text(f, "log", "-1", "--format=%an <%ae> %aI%n%cn <%ce>"));
    }

    [Fact]
    public async Task WhenTheIndexIsLocked_ShouldRefuseWithoutRemovingTheLock()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt"); await f.GitAsync("commit", "-m", "Initial");
        var head = await Text(f, "rev-parse", "HEAD");
        var snapshot = await f.ReadAsync();
        var lockPath = Path.Combine(f.Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "owned by another process", f.Token);

        var result = await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(snapshot, "Reworded", Amend: true), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(head, await Text(f, "rev-parse", "HEAD"));
        Assert.Equal("owned by another process", await File.ReadAllTextAsync(lockPath, f.Token));
        File.Delete(lockPath); // This test created this exact lock.
    }

    private static Task Put(GitFixture f, string path, string content) => File.WriteAllTextAsync(Path.Combine(f.Root, path), content, f.Token);
    private static async Task<string> Text(GitFixture f, params string[] args) => (await GitText(f, args)).TrimEnd('\n');
    private static async Task<string> GitText(GitFixture f, params string[] args)
    {
        var result = await f.RunAsync(args);
        Assert.Equal(0, result.ExitCode);
        return Encoding.UTF8.GetString(result.StandardOutput);
    }
    private static async Task<GitWriteResult> Amend(GitFixture f, string message) =>
        await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), message, Amend: true), f.Token);
}
