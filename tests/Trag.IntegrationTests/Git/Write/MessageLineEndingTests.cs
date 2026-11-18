using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Trag's message editors end a typed line with Windows' line break, and Git keeps a message as it is handed over: every
// message Trag writes reaches Git with a line feed alone at the end of each line.
public sealed class MessageLineEndingTests
{
    private const string Typed = "Fix the parser\r\n\r\nWhy it broke.\r\nCo-authored-by: Ana <ana@example.invalid>";

    private static Task<GitWriteResult> WriteAsync(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);

    [Fact]
    public async Task WhenACommitMessageWasTypedWithWindowsLineBreaks_ShouldCommitItWithLineFeeds()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n");
        await f.GitAsync("add", "a.txt");

        var result = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), Typed));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain('\r', await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenATagMessageWasTypedWithWindowsLineBreaks_ShouldKeepTheTagWithLineFeeds()
    {
        using var f = await GitFixture.CreateAsync("main");
        var head = await f.CommitAsync("Base");

        var result = await WriteAsync(f, new TagRequest(await f.ReadAsync(), new("v1", head, Typed)));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain('\r', await f.GitAsync("cat-file", "tag", "v1"));
    }

    [Fact]
    public async Task WhenAMergeMessageWasTypedWithWindowsLineBreaks_ShouldMakeTheMergeCommitWithLineFeeds()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("b.txt", "b\n");
        var feature = await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("c.txt", "c\n");
        await f.CommitAsync("Main");

        var result = await WriteAsync(f, new MergeRequest(await f.ReadAsync(), "feature", feature, true, Typed));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain('\r', await f.GitAsync("cat-file", "commit", "HEAD"));
    }
}
