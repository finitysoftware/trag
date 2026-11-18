using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Read;

public sealed class BareRepositoryReadTests
{
    // Under safe.bareRepository=explicit, as a careful configuration sets it, Git refuses a bare repository it is not
    // pointed at before it answers anything; the folder is still a bare repository, and the open says so.
    [Fact]
    public async Task WhenGitRefusesBareRepositoriesItIsNotPointedAt_ShouldStillSayTheFolderIsBare()
    {
        using var fixture = await GitFixture.CreateAsync();
        var bare = Path.Combine(fixture.Container, "bare.git");
        await fixture.GitAtAsync(fixture.Container, "init", "--bare", bare);
        var careful = await GitWrapper.WriteAsync(fixture.Container, "careful-git",
            [("GIT_CONFIG_COUNT", "1"), ("GIT_CONFIG_KEY_0", "safe.bareRepository"), ("GIT_CONFIG_VALUE_0", "explicit")]);
        var reader = new CliRepositoryReader(new GitProcessRunner(executable: new() { Path = careful }));

        var refused = await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(bare, fixture.Token));

        Assert.Equal(RepositoryError.BareRepository, refused.Error);
    }
}
