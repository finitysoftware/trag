using Trag.Domain.Catalog;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// A repository Git refuses for its ownership, as one owned by another account, is a repository Git does not trust, not
// a folder without one: initializing it would be the wrong answer.
public sealed class RepositoryProbeOwnershipTests
{
    [Fact]
    public async Task WhenGitDoesNotTrustTheFoldersOwnership_ShouldSaySoInsteadOfNotARepository()
    {
        using var fixture = await GitFixture.CreateAsync();
        // The empty safe.directory drops the exceptions a system configuration may hold, as the CI runner's trusts every folder.
        var otherOwner = await GitWrapper.WriteAsync(fixture.Container, "other-owner", [("GIT_TEST_ASSUME_DIFFERENT_OWNER", "1")], "-c", "safe.directory=");

        var probe = await new GitRepositoryProbe(new GitProcessRunner(executable: new() { Path = otherOwner })).ProbeAsync(fixture.Root, fixture.Token);

        Assert.Equal(RepositoryLinkStatus.Untrusted, probe.Status);
    }
}
