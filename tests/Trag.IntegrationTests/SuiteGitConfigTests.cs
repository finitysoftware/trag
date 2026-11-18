using Trag.TestSupport;

namespace Trag.IntegrationTests;

// The suite's Git reads no system configuration and no global one but the file the run names, so a test passes or fails
// the same on every machine. Origins are read with -z, which prints a path exactly as it was given.
public sealed class SuiteGitConfigTests
{
    [Fact]
    public async Task WhenGitRunsInTheSuite_ShouldReadNoConfigurationOfTheMachine()
    {
        using var f = await GitFixture.CreateAsync();
        var global = "file:" + Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");

        var entries = (await f.GitAtAsync(f.Container, "config", "--list", "--show-scope", "--show-origin", "-z"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Chunk(3);

        Assert.Empty(entries.Where(entry => entry[0] == "system" || entry[0] == "global" && entry[1] != global)
            .Select(entry => entry[0] + " " + entry[2].Split('\n')[0] + " " + entry[1]));
    }
}
