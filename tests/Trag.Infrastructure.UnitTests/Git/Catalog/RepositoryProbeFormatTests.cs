using Trag.Domain.Catalog;
using Trag.Infrastructure.Git.Catalog;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// A repository in a format this Git does not read, as a reftable repository Git 2.45 or newer made is to Git 2.43, is
// refused by Git for every command; it needs a newer Git, and initializing it would be the wrong answer.
public sealed class RepositoryProbeFormatTests
{
    [Fact]
    public async Task WhenTheRepositoryUsesAnExtensionThisGitDoesNotKnow_ShouldSayItNeedsANewerGit()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.repositoryformatversion", "1");
        await fixture.GitAsync("config", "extensions.tragFromTheFuture", "true");

        var probe = await new GitRepositoryProbe(fixture.Runner).ProbeAsync(fixture.Root, fixture.Token);

        Assert.Equal(RepositoryLinkStatus.NeedsNewerGit, probe.Status);
    }
}
