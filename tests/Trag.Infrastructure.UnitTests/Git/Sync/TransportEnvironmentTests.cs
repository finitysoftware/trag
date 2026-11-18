using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// What a transfer always runs with: no interactive prompt can appear, not even through an inherited askpass program, and
// the SSH command carries the timeouts of the variant that was chosen.
public sealed class TransportEnvironmentTests
{
    private static RemoteConfiguration Config(string variant = "ssh", string command = "ssh") =>
        new(new("origin", "https://example.invalid/repo.git", "https://example.invalid/repo.git", "config-1", ""), [], command, variant,
            "https://example.invalid/repo.git", "https://example.invalid/repo.git", []);

    [Fact]
    public void WhenATransferRunsForARemote_ShouldLetNoPromptAppearFromGitTheCredentialManagerOrSsh()
    {
        var environment = TransportEnvironment.Create(Config());
        Assert.Equal("1", environment["GIT_NO_LAZY_FETCH"]);
        Assert.Equal("1", environment["GIT_NO_REPLACE_OBJECTS"]);
        Assert.Equal("Never", environment["GCM_INTERACTIVE"]);
        Assert.Equal("false", environment["GCM_GUI_PROMPT"]);
        Assert.Equal("never", environment["SSH_ASKPASS_REQUIRE"]);
    }

    // Git asks an askpass program for a password even with terminal prompts off, and Git Bash exports SSH_ASKPASS, so a
    // Trag started from it would show that program's dialog on a transfer whose credentials are missing. A null entry
    // removes the variable the launching terminal set.
    [Theory]
    [InlineData("GIT_ASKPASS")]
    [InlineData("SSH_ASKPASS")]
    public void WhenTheLaunchingTerminalSetsAnAskpassProgram_ShouldKeepGitFromPromptingThroughIt(string variable)
    {
        Assert.Null(TransportEnvironment.Create(Config())[variable]);
        Assert.Null(TransportEnvironment.Create(null)[variable]);
    }

    // The account's askpass is added after the refusals, so it is the one Git asks.
    [Fact]
    public async Task WhenATransferAuthenticatesWithAnAccount_ShouldStillHandGitTragsOwnAskpass()
    {
        await using var broker = new CredentialBroker(new("https://example.invalid/"), "alice", "synthetic-value"u8, CancellationToken.None);
        var environment = TransportEnvironment.Create(Config());

        TransportEnvironment.Authenticated(environment, TransportEnvironment.Setup("origin"), broker, "Trag.exe");

        Assert.Equal(Path.GetFullPath("Trag.exe"), environment["GIT_ASKPASS"]);
    }

    // Git LFS prints nothing while it moves content unless asked to, and a transfer that stays silent past its limit is
    // stopped; clones, which have no remote yet, check LFS files out too.
    [Fact]
    public void WhenATransferRuns_ShouldHaveGitLfsReportItsProgress()
    {
        Assert.Equal("1", TransportEnvironment.Create(Config())["GIT_LFS_FORCE_PROGRESS"]);
        Assert.Equal("1", TransportEnvironment.Create(null)["GIT_LFS_FORCE_PROGRESS"]);
    }

    [Fact]
    public void WhenTheSshVariantIsOpenSsh_ShouldRunItInBatchModeWithItsTimeouts() =>
        Assert.Equal("ssh -oBatchMode=yes -oConnectTimeout=15 -oServerAliveInterval=15 -oServerAliveCountMax=2",
            TransportEnvironment.Create(Config())["GIT_SSH_COMMAND"]);

    [Fact]
    public void WhenTheSshVariantIsPlink_ShouldRunItWithItsOwnBatchOption() =>
        Assert.Equal("plink -batch", TransportEnvironment.Create(Config("plink", "plink"))["GIT_SSH_COMMAND"]);

    [Fact]
    public void WhenTheSshVariantIsTortoisePlink_ShouldTellGitTheVariant() =>
        Assert.Equal("tortoiseplink", TransportEnvironment.Create(Config("tortoiseplink", "tortoiseplink"))["GIT_SSH_VARIANT"]);

    [Fact]
    public void WhenATransferToARemoteIsSetUp_ShouldAllowOnlyTheProtocolsItNeedsAndNeverTreatTheRemoteAsAMirror()
    {
        var setup = TransportEnvironment.Setup("origin");
        Assert.Equal("protocol.allow=never", setup[1]);
        foreach (var protocol in new[] { "file", "ssh", "https", "http", "git" })
            Assert.Contains("protocol." + protocol + ".allow=always", setup);
        Assert.Contains("fetch.writeCommitGraph=false", setup);
        Assert.Contains("push.autoSetupRemote=false", setup);
        Assert.Contains("submodule.recurse=false", setup);
        Assert.Contains("remote.origin.mirror=false", setup);
        Assert.Equal(20, setup.Length);
        Assert.All(setup.Where((_, i) => i % 2 == 0), value => Assert.Equal("-c", value));
    }

    [Fact]
    public void WhenACloneHasNoRemoteYet_ShouldCarryTheSameRefusalsButNothingAboutARemote()
    {
        var environment = TransportEnvironment.Create(null);
        Assert.Equal(new Dictionary<string, string?> { ["GIT_NO_LAZY_FETCH"] = "1", ["GCM_INTERACTIVE"] = "Never", ["GCM_GUI_PROMPT"] = "false", ["SSH_ASKPASS_REQUIRE"] = "never",
            ["GIT_ASKPASS"] = null, ["SSH_ASKPASS"] = null, ["GIT_LFS_FORCE_PROGRESS"] = "1" }, environment);

        Assert.Equal(["-c", "protocol.allow=never", "-c", "protocol.file.allow=always", "-c", "protocol.ssh.allow=always",
            "-c", "protocol.https.allow=always", "-c", "protocol.http.allow=always", "-c", "protocol.git.allow=always",
            "-c", "submodule.recurse=false"], TransportEnvironment.Setup(null));
    }
}
