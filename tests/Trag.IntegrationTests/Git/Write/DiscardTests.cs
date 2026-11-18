using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Discarding against the real index and working tree: an index another program holds keeps the files as they are, and
// an untracked name reaches git clean literally, so a name that reads as a glob deletes only itself.
public sealed class DiscardTests
{
    [Fact]
    public async Task WhenTheIndexIsLocked_ShouldRefuseToRestoreAndChangeNothing()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\n");
        await f.CommitAsync("One", "a.txt");
        await f.WriteAsync("a.txt", "changed\n");
        var snapshot = await f.ReadAsync();
        var lockPath = Path.Combine(f.Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "owned by another process", f.Token);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(snapshot, [Named("a.txt")]), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal("changed\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"), f.Token));
        Assert.Equal("owned by another process", await File.ReadAllTextAsync(lockPath, f.Token));
        File.Delete(lockPath); // This test created this exact lock.
    }

    // "[x]" would match the single letter x as a glob, and so the other file.
    [Fact]
    public async Task WhenAnUntrackedFileHasBracketsSpacesAndNonAscii_ShouldDeleteExactlyThatFile()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("kept.txt", "kept\n");
        await f.CommitAsync("Kept", "kept.txt");
        await f.WriteAsync("a [x] č.txt", "chosen\n");
        await f.WriteAsync("a x č.txt", "matches the glob\n");
        var snapshot = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(snapshot, [Named("a [x] č.txt")]), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.False(File.Exists(Path.Combine(f.Root, "a [x] č.txt")));
        Assert.True(File.Exists(Path.Combine(f.Root, "a x č.txt")));
    }

    // One discard of a tracked file and of an untracked one under new/, both with the name: x.txt and new/x.txt, changed
    // and untracked beside them, are what "[x].txt" would match as a glob.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenFilesHaveAHostileName_ShouldRestoreTheTrackedOneAndDeleteTheUntrackedOneOnly(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "decoy\n");
        await f.WriteAsync(name, "one\n");
        await f.CommitAsync("Both");
        await f.WriteAsync("x.txt", "decoy changed\n");
        await f.WriteAsync(name, "changed\n");
        await f.WriteAsync("new/x.txt", "decoy\n");
        await f.WriteAsync("new/" + name, "new\n");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(await f.ReadAsync(), [Named(name), Named("new/" + name)]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, name), f.Token));
        Assert.False(File.Exists(Path.Combine(f.Root, "new", name)));
        Assert.Equal("decoy changed\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "x.txt"), f.Token));
        Assert.True(File.Exists(Path.Combine(f.Root, "new", "x.txt")));
    }

    // The index holds a Git LFS pointer; restoring the file runs Git LFS's smudge, so the content comes back, not the
    // pointer.
    [Fact]
    public async Task WhenAnLfsFileIsDiscarded_ShouldSmudgeItBackThroughGitLfs()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await f.WriteAsync("asset.bin", "fixture\n");
        await f.CommitAsync("Asset", ".gitattributes", "asset.bin");
        await f.WriteAsync("asset.bin", "changed\n");
        var snapshot = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new DiscardRequest(snapshot, [Named("asset.bin")]), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("fixture\n", (await File.ReadAllTextAsync(Path.Combine(f.Root, "asset.bin"), f.Token)).Replace("\r\n", "\n"));
    }

    private static GitPath Named(string path) => new(Encoding.UTF8.GetBytes(path));
}
