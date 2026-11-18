using Trag.Domain.Catalog;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Catalog;

// A clone into a folder named by the corpus of hostile names: Git clones into exactly the folder asked for, with nothing
// beside it, and a folder Git cannot work in is refused, the target folder not left behind.
public sealed class CloneTests
{
    [Theory]
    [MemberData(nameof(HostileNameRows.UsableFolders), MemberType = typeof(HostileNameRows))]
    public async Task WhenTheTargetFolderHasAHostileName_ShouldCloneIntoExactlyThatFolder(string label)
    {
        using var f = await SourceAsync();
        var target = Path.Combine(f.Container, "clones", TestNames.Find(label).Path);
        var catalog = Catalog(f);

        var result = await catalog.CloneAsync(new(f.Root, target), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(Path.GetFileName(target), Path.GetFileName(Assert.Single(Directory.EnumerateDirectories(Path.GetDirectoryName(target)!))));
        var probe = await catalog.ProbeAsync(target, f.Token);
        Assert.Equal((RepositoryLinkStatus.Valid, Path.GetFullPath(target)), (probe.Status, probe.Root));
        Assert.True(File.Exists(Path.Combine(target, "file.txt")));
    }

    // Git for Windows cannot create a folder with a reserved device name for the clone, and a clone into a folder past
    // MAX_PATH fails at its checkout.
    [Theory]
    [MemberData(nameof(HostileNameRows.UnusableFolders), MemberType = typeof(HostileNameRows))]
    public async Task WhenGitCannotWorkInTheTargetFolder_ShouldRefuseTheCloneAndLeaveNoTargetFolder(string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await SourceAsync();
        var target = Path.Combine(f.Container, "clones", TestNames.Find(label).Path);

        var result = await Catalog(f).CloneAsync(new(f.Root, target), f.Token);

        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(target));
    }

    // file.txt committed, so a clone has a file to check out.
    private static async Task<GitFixture> SourceAsync()
    {
        var f = await GitFixture.CreateAsync();
        await f.WriteAsync("file.txt", "content\n");
        await f.CommitAsync("Initial", "file.txt");
        return f;
    }

    private static RepositoryCatalogFacade Catalog(GitFixture f) => new(f.Runner, new RepositoryCatalogStore(Path.Combine(f.Container, "repositories.json")));
}
