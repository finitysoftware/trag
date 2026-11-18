using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// Without ssh.variant Git tells the SSH program by the name of the command's first word; Trag takes the variant Git
// would, so Plink is never handed OpenSSH's options.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class RemoteSshVariantTests
{
    [Theory]
    [InlineData("plink -i key.ppk", "plink")]
    [InlineData("'C:/Program Files/PuTTY/PLINK.EXE'", "plink")]
    [InlineData("'C:\\Program Files\\PuTTY\\plink.exe'", "plink")]
    [InlineData("\"C:/Program Files/TortoiseGit/bin/TortoisePlink.exe\" -batch", "tortoiseplink")]
    [InlineData("ssh -i key", "ssh")]
    public async Task WhenNoVariantIsConfigured_ShouldTellItByTheCommandsProgramAsGitDoes(string command, string variant)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("remote", "add", "origin", "git@example.invalid:team/project.git");
        await fixture.GitAsync("config", "core.sshCommand", command);

        var config = await new RemoteConfigurationReader(new PresetGitRunner(fixture.Runner, LocalGitPreset.Writing), new GitRefNameValidator(fixture.Runner))
            .ReadConfigurationAsync(await fixture.ReadAsync(), "origin", fixture.Token);

        Assert.Equal(variant, config.SshVariant);
    }

    // Git takes GIT_SSH_COMMAND before core.sshCommand, and tells the variant by it.
    [Fact]
    public async Task WhenTheLaunchingTerminalSetsGitSshCommand_ShouldTakeItBeforeCoreSshCommandAsGitDoes()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("remote", "add", "origin", "git@example.invalid:team/project.git");
        await fixture.GitAsync("config", "core.sshCommand", "plink -i key.ppk");
        var reader = new RemoteConfigurationReader(new PresetGitRunner(fixture.Runner, LocalGitPreset.Writing), new GitRefNameValidator(fixture.Runner));
        var snapshot = await fixture.ReadAsync();

        var previous = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        Environment.SetEnvironmentVariable("GIT_SSH_COMMAND", "ssh -i env-key");
        RemoteConfiguration config;
        try { config = await reader.ReadConfigurationAsync(snapshot, "origin", fixture.Token); }
        finally { Environment.SetEnvironmentVariable("GIT_SSH_COMMAND", previous); }

        Assert.Equal(("ssh -i env-key", "ssh"), (config.SshCommand, config.SshVariant));
    }
}
