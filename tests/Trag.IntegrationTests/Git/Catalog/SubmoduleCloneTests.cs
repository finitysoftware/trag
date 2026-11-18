using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Catalog;

// A clone of a repository with a submodule, with its submodules or without them; one left out, or one Git does not clone,
// is the submodule Update submodules offers afterwards.
public sealed class SubmoduleCloneTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenARepositoryWithASubmoduleIsCloned_ShouldCheckOutTheSubmoduleOnlyWhenAskedTo(bool submodules)
    {
        var (f, server) = await SubmoduleFixture.CreateServedAsync();
        using var _ = server;
        using var __ = f;
        var target = Path.Combine(f.Container, "clones", "super");

        var result = await Catalog(f).CloneAsync(new(f.Root, target, Submodules: submodules), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(submodules ? [] : ["sub"], (await f.ReadAsync(target)).SubmodulesToUpdate);
    }

    // A submodule Git cannot clone leaves the clone of the repository itself, with Git's words, and the submodule is left
    // to Update submodules.
    [Fact]
    public async Task WhenASubmoduleCannotBeCloned_ShouldKeepTheCloneWithTheSubmoduleLeftForUpdate()
    {
        var (f, server) = await SubmoduleFixture.CreateServedAsync();
        using var _ = server;
        using var __ = f;
        Directory.Move(SubmoduleFixture.Origin(f), SubmoduleFixture.Origin(f) + "-gone");
        var target = Path.Combine(f.Container, "clones", "super");

        var result = await Catalog(f).CloneAsync(new(f.Root, target, Submodules: true), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Contains("submodule path", result.Details);
        Assert.Equal(["sub"], (await f.ReadAsync(target)).SubmodulesToUpdate);
    }

    // A submodule's address comes from the repository being cloned, so a local repository is never cloned for it.
    [Fact]
    public async Task WhenASubmoduleIsALocalRepository_ShouldKeepTheCloneWithoutIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var target = Path.Combine(f.Container, "clones", "super");

        var result = await Catalog(f).CloneAsync(new(f.Root, target, Submodules: true), f.Token);

        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Contains("transport 'file' not allowed", result.Details);
        Assert.Equal(["sub"], (await f.ReadAsync(target)).SubmodulesToUpdate);
    }

    private static RepositoryCatalogFacade Catalog(GitFixture f) =>
        new(f.Runner, new RepositoryCatalogStore(Path.Combine(f.Container, "repositories.json")));
}
