using System.Buffers.Binary;
using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// An index Git splits into .git/index and a shared .git/sharedindex.<hash>, and an index version 4 written without its
// trailing checksum, as feature.manyFiles asks for. Trag copies only .git/index into its private index beside it and
// publishes the copy back, so every write that goes through the private index has to leave Git an index it reads;
// discards run against the real index.
// Each row sets the other key itself, since Git 2.55 loses staged entries when a split index skips its checksum.
public sealed class IndexFormatTests
{
    private const string Before = "a\nb\nc\nd\ne\nf\ng\nh\ni\n";
    private const string After = "a\nb\nc\nd\nE\nf\ng\nh\nI\n";
    private const string Committed = "a\nb\nc\nd\nE\nf\ng\nh\ni\n";

    [Theory]
    [InlineData(true, "core.splitIndex=true", "index.version=4", "index.skipHash=false")]
    [InlineData(false, "feature.manyFiles=true", "core.splitIndex=false")]
    public async Task WhenTheIndexIsSplitOrSkipsItsChecksum_ShouldStageCommitAndUnstageThroughThePrivateIndexAndDiscard(bool split, params string[] settings)
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        foreach (var setting in settings) await f.GitAsync(["config", .. setting.Split('=')]);
        await f.WriteAsync("sample.txt", Before); await f.WriteAsync("one.txt", "one\n");
        await f.CommitAsync("Base", "sample.txt", "one.txt");
        await f.WriteAsync("sample.txt", After); await f.WriteAsync("one.txt", "one\nmore\n"); await f.WriteAsync("two.txt", "two\n");
        var writer = new GitWriteRouter(f.Runner);

        Succeeded(await HunkAsync(f, writer, "-e", PatchAction.Stage));
        Succeeded(await writer.ExecuteAsync(new StageRequest(await f.ReadAsync(), [Named("one.txt"), Named("two.txt")]), f.Token));
        Succeeded(await writer.ExecuteAsync(new CommitRequest(await f.ReadAsync(), "Formats"), f.Token));
        Succeeded(await HunkAsync(f, writer, "-i", PatchAction.Stage));
        Succeeded(await HunkAsync(f, writer, "-i", PatchAction.Unstage));
        var index = await f.GitAsync("show", ":sample.txt") + "\n";
        Succeeded(await HunkAsync(f, writer, "-i", PatchAction.Discard));
        await f.WriteAsync("one.txt", "one\nmore\nagain\n");
        Succeeded(await writer.ExecuteAsync(new DiscardRequest(await f.ReadAsync(), [Named("one.txt")]), f.Token));

        Assert.Equal((Committed, "one\nmore\n", "two\n"),
            (await f.GitAsync("show", "HEAD:sample.txt") + "\n", await f.GitAsync("show", "HEAD:one.txt") + "\n", await f.GitAsync("show", "HEAD:two.txt") + "\n"));
        Assert.Equal(Committed, index);
        Assert.Equal("", await f.GitAsync("status", "--porcelain"));
        Assert.Equal(split, await f.GitAsync("rev-parse", "--shared-index-path") != "");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token);
        Assert.Equal((4u, !split), (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)), bytes[^20..].All(value => value == 0)));
        await f.GitAsync("fsck", "--no-progress");
    }

    private static void Succeeded(GitWriteResult result) =>
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);

    // The hunk of sample.txt that starts with the line, from the side the action reads, sent as Trag sends it.
    private static async Task<GitWriteResult> HunkAsync(GitFixture f, GitWriteRouter writer, string line, PatchAction action)
    {
        var fromIndex = action == PatchAction.Unstage;
        var file = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "sample.txt" && (fromIndex ? file.IsStaged : file.IsUnstaged));
        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, file, fromIndex, false, f.Token);
        var lines = DiffBlocks.At(diff.Lines, diff.Lines.ToList().FindIndex(candidate => candidate.Text == line))!.Value.Lines;
        return await writer.ExecuteAsync(new PatchRequest(await f.ReadAsync(), Named("sample.txt"), HunkPatch.For(diff.Lines, lines, Named("sample.txt"), action), action, Lines: false), f.Token);
    }

    private static GitPath Named(string path) => new(Encoding.UTF8.GetBytes(path));
}
