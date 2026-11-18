using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Git refuses a repository another account owns, and one reached through a network share, until safe.directory names
// it. GIT_TEST_ASSUME_DIFFERENT_OWNER makes the fixture another account's, and the administrative share of its drive
// reaches it as a share; safe.directory comes through GIT_CONFIG_COUNT, as from the global configuration. Both go into
// this process's environment, so the tests run alone.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SafeDirectoryTests
{
    [Theory]
    [InlineData("another owner")]
    [InlineData("share")]
    public async Task WhenTheRepositoryIsOwnedByAnotherUser_ShouldSayUnsafeAndOpenAfterSafeDirectoryIsAdded(string story)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await GitFixture.CreateAsync();
        await f.CommitAsync("first");
        var path = story == "share" ? @"\\localhost\" + f.Root[0] + "$" + f.Root[2..] : f.Root;
        var safe = (story == "share" ? "%(prefix)/" : "") + path.Replace('\\', '/');
        var reader = new CliRepositoryReader(new GitProcessRunner());
        using var owner = story == "share" ? null : new EnvironmentVariables(("GIT_TEST_ASSUME_DIFFERENT_OWNER", "1"));

        var refused = await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(path, f.Token));
        using var trusted = new EnvironmentVariables(("GIT_CONFIG_COUNT", "1"), ("GIT_CONFIG_KEY_0", "safe.directory"), ("GIT_CONFIG_VALUE_0", safe));
        var opened = await reader.ReadAsync(path, f.Token);

        Assert.Equal((RepositoryError.UnsafeDirectory, "fixture"), (refused.Error, opened.Branch));
    }
}
