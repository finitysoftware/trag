using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Platform.Storage;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Catalog;

// A clone of a repository with Git LFS files downloads their content as a step of its own, after the repository itself,
// so its credentials are asked for when that step starts; content it cannot download leaves no half checked-out clone
// behind, so the same clone can be tried again.
public sealed class LfsCloneTests
{
    private static async Task<GitFixture> SourceAsync()
    {
        var f = await GitFixture.CreateAsync();
        try
        {
            await LfsConfig.SetAsync(f, f.Root, installed: true);
            await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
            await f.WriteAsync("asset.bin", "fixture\n");
            await f.CommitAsync("Asset", ".gitattributes", "asset.bin");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static RepositoryCatalogFacade Catalog(GitFixture f) =>
        new(f.Runner, new RepositoryCatalogStore(Path.Combine(f.Container, "repositories.json")));

    [Fact]
    public async Task WhenAnLfsRepositoryIsCloned_ShouldCheckOutTheContentOfItsLfsFiles()
    {
        using var f = await SourceAsync();
        var target = Path.Combine(f.Container, "clones", "lfs");
        var progress = new ProgressLog();

        var result = await Catalog(f).CloneAsync(new(f.Root, target, Progress: progress), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("fixture\n", await File.ReadAllTextAsync(Path.Combine(target, "asset.bin"), f.Token));
        Assert.Contains(SyncOutcomeText.FetchingLfs, progress.Reports);
    }

    // Git LFS has nothing to do in a repository without LFS files, or without any commit yet.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheRepositoryHasNoLfsFiles_ShouldCloneWithoutGitLfs(bool commits)
    {
        using var f = await GitFixture.CreateAsync();
        if (commits) { await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Plain", "a.txt"); }
        var target = Path.Combine(f.Container, "clones", "plain");
        var progress = new ProgressLog();

        var result = await Catalog(f).CloneAsync(new(f.Root, target, Progress: progress), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.DoesNotContain(SyncOutcomeText.FetchingLfs, progress.Reports);
    }

    [Fact]
    public async Task WhenTheLfsContentCannotBeDownloaded_ShouldSaySoAndLeaveNoClone()
    {
        using var f = await SourceAsync();
        Directory.Delete(Path.Combine(f.Root, ".git", "lfs", "objects"), true);
        var target = Path.Combine(f.Container, "clones", "lfs");

        var result = await Catalog(f).CloneAsync(new(f.Root, target), f.Token);

        Assert.Equal((false, CloneText.LfsNotDownloaded), (result.Succeeded, result.Message));
        Assert.False(Directory.Exists(target));
    }
}
