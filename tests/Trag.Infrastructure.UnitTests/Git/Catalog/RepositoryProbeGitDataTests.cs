using Trag.Domain.Catalog;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// Folders that hold a repository's Git data or a bare repository, also where Git cannot answer the probe's question: git
// init there would nest a new repository inside them, so none is a folder to initialize.
public sealed class RepositoryProbeGitDataTests
{
    // Removing a submodule keeps its Git data, whose working tree setting then points at a folder that is gone.
    [Fact]
    public async Task WhenARemovedSubmodulesGitDataPointsAtAWorkingTreeThatIsGone_ShouldSayItHoldsGitData()
    {
        using var fixture = await GitFixture.CreateAsync();
        var source = Path.Combine(fixture.Container, "sub-source");
        await fixture.GitAtAsync(fixture.Container, "init", source);
        await fixture.GitAtAsync(source, "-c", "user.name=Trag Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "-m", "Sub");
        await fixture.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(source).AbsoluteUri, "sub");
        await fixture.GitAsync("commit", "-m", "Add sub");
        await fixture.GitAsync("rm", "sub");

        var probe = await new GitRepositoryProbe(fixture.Runner).ProbeAsync(Path.Combine(fixture.Root, ".git", "modules", "sub"), fixture.Token);

        Assert.Equal(RepositoryLinkStatus.GitDirectory, probe.Status);
    }

    // git init --separate-git-dir keeps the Git data in a folder of any name.
    [Fact]
    public async Task WhenTheGitDataIsKeptApartFromTheWorkingCopy_ShouldSayItHoldsGitData()
    {
        using var fixture = await GitFixture.CreateAsync();
        var data = Path.Combine(fixture.Container, "data");
        await fixture.GitAtAsync(fixture.Container, "init", "--separate-git-dir", data, Path.Combine(fixture.Container, "apart"));

        var probe = await new GitRepositoryProbe(fixture.Runner).ProbeAsync(data, fixture.Token);

        Assert.Equal(RepositoryLinkStatus.GitDirectory, probe.Status);
    }

    // Under safe.bareRepository=explicit, as a careful configuration sets it, Git works in a bare repository only when told
    // which one; the folder is still a bare repository.
    [Fact]
    public async Task WhenGitRefusesBareRepositoriesItIsNotPointedAt_ShouldStillSayTheFolderIsBare()
    {
        using var fixture = await GitFixture.CreateAsync();
        var bare = Path.Combine(fixture.Container, "bare.git");
        await fixture.GitAtAsync(fixture.Container, "init", "--bare", bare);
        var careful = await GitWrapper.WriteAsync(fixture.Container, "careful-git",
            [("GIT_CONFIG_COUNT", "1"), ("GIT_CONFIG_KEY_0", "safe.bareRepository"), ("GIT_CONFIG_VALUE_0", "explicit")]);

        var probe = await new GitRepositoryProbe(new GitProcessRunner(executable: new() { Path = careful })).ProbeAsync(bare, fixture.Token);

        Assert.Equal(RepositoryLinkStatus.Bare, probe.Status);
    }
}
