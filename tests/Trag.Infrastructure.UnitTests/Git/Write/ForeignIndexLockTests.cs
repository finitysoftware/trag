using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// A tag, a branch rename or a new remote never takes .git/index.lock, so a lock found after such a write was stopped is
// another program's, whenever that program took it.
public sealed class ForeignIndexLockTests
{
    [Theory]
    [InlineData("tag")]
    [InlineData("rename")]
    [InlineData("remote")]
    public async Task WhenAnotherProgramTakesTheIndexLockDuringAWriteThatTakesNoneAndCancelFollows_ShouldLeaveThatLock(string write)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("a.txt", "one\n");
        var head = await fixture.CommitAsync("first");
        await fixture.GitAsync("branch", "other");
        var lockPath = Path.Combine(fixture.Root, ".git", "index.lock");
        var before = await RefsAndRemotesAsync(fixture);
        using var cancellation = new CancellationTokenSource();
        // Once Git has made the change, the other program takes the lock and the user presses Cancel.
        var router = new GitWriteRouter(new GitProcessRunner(exited: _ =>
        {
            if (cancellation.IsCancellationRequested || RefsAndRemotesAsync(fixture).GetAwaiter().GetResult() == before) return;
            File.WriteAllBytes(lockPath, []);
            cancellation.Cancel();
        }));
        var snapshot = await fixture.ReadAsync();
        GitWriteRequest request = write switch
        {
            "tag" => new TagRequest(snapshot, new("v1", head)),
            "rename" => new BranchRenameRequest(snapshot, "other", "renamed"),
            _ => new RemoteAddRequest(snapshot, "upstream", "https://example.invalid/a.git")
        };

        var result = await router.ExecuteAsync(request, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested, result.Outcome + ": " + result.Message);
        Assert.True(File.Exists(lockPath));
    }

    private static async Task<string> RefsAndRemotesAsync(GitFixture fixture) =>
        await fixture.GitAsync("for-each-ref", "--format=%(refname)") + "\n" + await fixture.GitAsync("remote");
}
