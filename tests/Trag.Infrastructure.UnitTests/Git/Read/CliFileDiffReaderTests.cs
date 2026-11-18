using Trag.Domain.Diff;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.State;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Read;

public sealed class CliFileDiffReaderTests
{
    private readonly CliFileDiffReader reader = new(new GitProcessRunner(executable: new() { Path = "trag-missing-git" }));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WhenTheRenameSourceIsInvalid_ShouldRefuseItBeforeGitOrTheFilesystemIsReached(bool fromIndex, bool preview)
    {
        var file = new FileStatus(new("valid.txt"u8.ToArray()), 'R', '.', new([0xff, 0xfe]));
        var result = await reader.ReadAsync("missing-repository", file, fromIndex, preview, default);
        Assert.Equal(DiffContentKind.Unavailable, result.Kind);
        Assert.Contains("non-UTF-8", result.Message);
        Assert.Empty(result.Lines);
        Assert.Equal(new byte[] { 0xff, 0xfe }, file.OriginalPath!.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenThePathLeadsOutOfTheRepository_ShouldRefuseItBeforeRunningGit(bool fromIndex)
    {
        var file = new FileStatus(new("../outside.txt"u8.ToArray()), 'M', 'M');
        var error = await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync(Path.GetTempPath(), file, fromIndex, true, default));
        Assert.Equal("Path outside repository.", error.Message);
    }

    // A hunk staged with git apply --cached leaves its index entry without the file's stat data. Once the file has nothing
    // left to stage, Git's diff of the working file refreshes that entry and writes the index, the very state every write
    // compares with the one the user was shown; so a read must leave the index as it found it.
    [Fact]
    public async Task WhenTheWorkingDiffIsReadAfterTheLastHunkWasStaged_ShouldLeaveTheIndexAsItWas()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("a.txt", "one\n");
        await f.CommitAsync("One", "a.txt");
        await f.WriteAsync("a.txt", "two\n");
        var patch = Path.Combine(f.Container, "hunk.patch");
        await f.GitAsync("diff", "--output=" + patch);
        await f.GitAsync("apply", "--cached", patch);
        var gitDirectory = Path.Combine(f.Root, ".git");
        var before = await IndexFingerprint.ReadAsync(gitDirectory, f.Token);

        await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, new FileStatus(new("a.txt"u8.ToArray()), 'M', 'M'), false, false, f.Token);

        Assert.Equal(before, await IndexFingerprint.ReadAsync(gitDirectory, f.Token));
    }
}
