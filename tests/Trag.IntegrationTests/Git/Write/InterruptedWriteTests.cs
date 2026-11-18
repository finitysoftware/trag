using Trag.Application.Workspace;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Cancel pressed while Git itself writes the working tree on the real index: a switch, a hard reset, a discard. Git holds
// .git/index.lock for the whole command, and a Git that is killed never removes it; a commit already keeps its own lock
// apart (WhenACommitIsCancelledDuringAHook). Here Git is held while it writes z.txt, in a smudge filter that waits.
public sealed class InterruptedWriteTests
{
    // base: a.txt and z.txt, z.txt through the "slow" filter. other and main each change both files; main is checked out.
    // The filter is configured last, so the setup's own checkouts do not wait.
    private static async Task<GitFixture> RepositoryAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            // A parallel checkout writes a.txt after the filtered z.txt, so a.txt is gone, not rewritten, when Cancel comes.
            await f.GitAsync("config", "checkout.workers", "1");
            await f.WriteAsync(".gitattributes", "z.txt filter=slow\n");
            await f.WriteAsync("a.txt", "a\n"); await f.WriteAsync("z.txt", "z\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-q", "-c", "other");
            await f.WriteAsync("a.txt", "a other\n"); await f.WriteAsync("z.txt", "z other\n"); await f.CommitAsync("Other");
            await f.GitAsync("switch", "-q", "main");
            await f.WriteAsync("a.txt", "a main\n"); await f.WriteAsync("z.txt", "z main\n"); await f.CommitAsync("Main");
            await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "slow-smudge.sh"), "echo ready > .git/trag-filter-ready\nsleep 30\ncat\n", f.Token);
            await f.GitAsync("config", "filter.slow.smudge", "sh .git/slow-smudge.sh");
            await f.GitAsync("config", "filter.slow.clean", "cat");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static async Task<GitWriteRequest> RequestAsync(GitFixture f, string write)
    {
        if (write == "discard")
        {
            await f.WriteAsync("a.txt", "a edited\n"); await f.WriteAsync("z.txt", "z edited\n");
            return new DiscardRequest(await f.ReadAsync(), [Snapshots.Path("a.txt"), Snapshots.Path("z.txt")]);
        }
        var snapshot = await f.ReadAsync();
        return write == "switch"
            ? new BranchRequest(snapshot, new("other"), BranchAction.Switch)
            : new ResetRequest(snapshot, await f.GitAsync("rev-parse", "HEAD~1"), ResetMode.Hard);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("reset")]
    [InlineData("discard")]
    public async Task WhenAWriteIsCanceledWhileGitWritesTheWorkingTree_ShouldLeaveNoIndexLockBehind(string write)
    {
        using var f = await RepositoryAsync();
        var request = await RequestAsync(f, write);
        using var cancellation = new CancellationTokenSource();

        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(request, cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-filter-ready")))
        {
            if (pending.IsCompleted) throw new InvalidOperationException((await pending).Message + " " + (await pending).Details);
            await Task.Delay(30, f.Token);
        }
        cancellation.Cancel();
        var result = await pending;

        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")), result.Outcome + ": " + result.Message);
    }

    // Git writes a.txt before it waits on z.txt, so the working tree is already half the other commit's when Cancel comes.
    // An outcome that says nothing changed, or that Git refused, sends the user on without looking; this one needs review.
    // What the user is told is what the workspace answers once the write is verified.
    [Theory]
    [InlineData("switch")]
    [InlineData("reset")]
    [InlineData("discard")]
    public async Task WhenAWriteIsCanceledAfterGitRewroteSomeFiles_ShouldAskForAReviewInsteadOfSayingNothingChanged(string write)
    {
        using var f = await RepositoryAsync();
        var request = await RequestAsync(f, write);
        var before = await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"), f.Token);
        using var cancellation = new CancellationTokenSource();

        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(f.Runner), writer: new GitWriteRouter(f.Runner));
        await workspace.OpenAsync(f.Root, f.Token);

        var pending = workspace.WriteAsync(request, cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-filter-ready")))
        {
            if (pending.IsCompleted) throw new InvalidOperationException((await pending).Message + " " + (await pending).Details);
            await Task.Delay(30, f.Token);
        }
        cancellation.Cancel();
        var result = await pending;

        Assert.NotEqual(before, await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"), f.Token));
        Assert.True(result.Outcome == GitWriteOutcome.Uncertain, result.Outcome + ": " + result.Message);
    }
}
