using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// A write the file system stops ends with the router's one sentence for every kind of write, so it speaks of no commit
// message: most writes have none, and a commit keeps its message after any refusal.
public sealed class WriteNotCompletedTests
{
    [Fact]
    public async Task WhenTheGitignoreIsReadOnly_ShouldSayTheIgnoreDidNotCompleteWithoutSpeakingOfACommitMessage()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n");
        await f.CommitAsync("first", "a.txt");
        await f.WriteAsync(".gitignore", "*.tmp\n");
        await f.WriteAsync("out.log", "x\n");
        var gitignore = Path.Combine(f.Root, ".gitignore");
        File.SetAttributes(gitignore, FileAttributes.ReadOnly);
        try
        {
            var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new IgnoreRequest(await f.ReadAsync(), Snapshots.Path("out.log"), "out.log"), f.Token);

            Assert.Equal((GitWriteOutcome.Rejected, WriteText.NotCompleted), (result.Outcome, result.Message));
            Assert.DoesNotContain("commit message", result.Message);
        }
        finally { File.SetAttributes(gitignore, FileAttributes.Normal); }
    }
}
