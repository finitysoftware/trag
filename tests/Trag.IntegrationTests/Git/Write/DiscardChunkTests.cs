using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// git clean takes its file names on the command line, so a large selection is deleted in bounded chunks under one message.
public sealed class DiscardChunkTests
{
    [Fact]
    public async Task WhenTheSelectionIsLargerThanOneChunk_ShouldDiscardItInFullUnderOneMessage()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("kept.txt", "base\n");
        await fixture.CommitAsync("first", "kept.txt");
        await fixture.WriteAsync("staged.txt", "staged\n");
        await fixture.GitAsync("add", "--", "staged.txt");
        for (var i = 0; i < 40; i++) await fixture.WriteAsync($"scratch-{i:D2}.txt", "temporary\n");

        var paths = Enumerable.Range(0, 40).Select(i => new GitPath(Encoding.UTF8.GetBytes($"scratch-{i:D2}.txt"))).ToArray();
        var result = await new GitWriteRouter(fixture.Runner)
            .ExecuteAsync(new DiscardRequest(await fixture.ReadAsync(), paths), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Discarded changes in 40 files. Untracked files were deleted. Staged content was kept.", result.Message);
        Assert.Empty(Directory.GetFiles(fixture.Root, "scratch-*.txt"));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "kept.txt")));

        var after = await fixture.ReadAsync();
        Assert.True(Assert.Single(after.Files).IsStaged);
    }
}
