using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// The remotes of a repository added, renamed, given another address and removed in its own configuration; nothing
// reaches a server.
public sealed class RemoteTests
{
    private const string Address = "https://github.com/acme/trag.git";

    [Fact]
    public async Task WhenARemoteIsAdded_ShouldConfigureItUnderTheNameAtTheAddress()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");

        var result = await WriteAsync(fixture, expected => new RemoteAddRequest(expected, "upstream", Address));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(RemoteText.Added("upstream"), result.Message);
        Assert.Equal(Address, await fixture.GitAsync("config", "--get", "remote.upstream.url"));
        Assert.Contains((await fixture.ReadAsync()).Remotes, remote => remote.Name == "upstream");
    }

    [Fact]
    public async Task WhenARemoteIsRenamedAndGivenAnotherAddress_ShouldKeepOnlyTheNewNameAtTheNewAddress()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "upstream", Address);

        var result = await WriteAsync(fixture, expected => new RemoteEditRequest(expected, "upstream", "acme", "git@github.com:acme/trag.git"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(RemoteText.Saved("acme"), result.Message);
        Assert.Equal("git@github.com:acme/trag.git", await fixture.GitAsync("config", "--get", "remote.acme.url"));
        Assert.DoesNotContain((await fixture.ReadAsync()).Remotes, remote => remote.Name == "upstream");
    }

    [Fact]
    public async Task WhenARemoteIsOnlyRenamed_ShouldKeepItsAddress()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "upstream", Address);

        var result = await WriteAsync(fixture, expected => new RemoteEditRequest(expected, "upstream", "acme", null));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(Address, await fixture.GitAsync("config", "--get", "remote.acme.url"));
    }

    // git remote set-url changes the address fetches use; a remote configured with its own push URL keeps pushing there,
    // and the result says where, since the dialog edits only the one address.
    [Fact]
    public async Task WhenARemoteWithASeparatePushUrlIsEdited_ShouldSayThePushUrlStays()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "upstream", Address);
        await fixture.GitAsync("config", "remote.upstream.pushurl", "https://github.com/acme/push.git");

        var result = await WriteAsync(fixture, expected => new RemoteEditRequest(expected, "upstream", "upstream", "git@github.com:acme/trag.git"));

        Assert.Equal((GitWriteOutcome.Succeeded, "Remote upstream saved. Pushes still go to https://github.com/acme/push.git."), (result.Outcome, result.Message));
        Assert.Equal("https://github.com/acme/push.git", await fixture.GitAsync("config", "--get", "remote.upstream.pushurl"));
    }

    // The remote the branch tracks is renamed: Git moves the branch's tracking to the new name, and the sync target follows
    // it there.
    [Fact]
    public async Task WhenTheTrackingRemoteIsRenamed_ShouldFollowInBranchConfigAndTheSyncTarget()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "origin", Address);
        await fixture.GitAsync("config", "branch.main.remote", "origin");
        await fixture.GitAsync("config", "branch.main.merge", "refs/heads/main");
        var before = await fixture.ReadAsync();
        var target = Trag.Domain.Sync.SyncTargetSelection.Empty.WithSnapshot(before);

        var result = await WriteAsync(fixture, expected => new RemoteEditRequest(expected, "origin", "upstream", null));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        var after = await fixture.ReadAsync();
        Assert.Equal(("origin", "upstream"), (before.TrackingRemote, after.TrackingRemote));
        Assert.Equal("upstream", target.WithSnapshot(after).Remote?.Name);
    }

    // Its remote-tracking branches go with it.
    [Fact]
    public async Task WhenARemoteIsRemoved_ShouldTakeItAndItsRemoteTrackingBranchesOutOfTheRepository()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "upstream", Address);
        await fixture.GitAsync("update-ref", "refs/remotes/upstream/main", "HEAD");

        var result = await WriteAsync(fixture, expected => new RemoteRemoveRequest(expected, "upstream"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(RemoteText.Removed("upstream"), result.Message);
        Assert.Empty((await fixture.ReadAsync()).Remotes);
        Assert.Equal("", await fixture.GitAsync("for-each-ref", "refs/remotes/upstream"));
    }

    // Another program holds the configuration: Git refuses, and the remotes stay as they were.
    [Fact]
    public async Task WhenGitCannotWriteTheConfiguration_ShouldRefuseAndLeaveTheRemotesAsTheyWere()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        var expected = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "config.lock"), "");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RemoteAddRequest(expected, "upstream", Address), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RemoteText.Failed), (result.Outcome, result.Message));
        File.Delete(Path.Combine(fixture.Root, ".git", "config.lock"));
        Assert.Empty((await fixture.ReadAsync()).Remotes);
    }

    // Git writes a remote into the configuration through config.lock; a change stopped while Git holds it must not leave
    // the lock behind, or every later change of the configuration is refused.
    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("remove")]
    public async Task WhenTheChangeIsCanceledWhileGitRewritesTheConfiguration_ShouldLeaveNoLock(string change)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        if (change != "add") await fixture.GitAsync("remote", "add", "upstream", Address);
        await ConfigurationLock.LengthenAsync(fixture);
        var expected = await fixture.ReadAsync();
        GitWriteRequest request = change switch
        {
            "add" => new RemoteAddRequest(expected, "acme", Address),
            "edit" => new RemoteEditRequest(expected, "upstream", "acme", "git@github.com:acme/trag.git"),
            _ => new RemoteRemoveRequest(expected, "upstream")
        };

        await ConfigurationLock.CancelWhileHeldAsync(fixture, request);

        Assert.False(File.Exists(Path.Combine(fixture.Root, ".git", "config.lock")));
    }

    // Git takes the upstream off every branch that tracks the remote before it deletes the remote's refs, so a removal it
    // refuses at a ref another program locks has changed the branch already, though the remote stays.
    [Fact]
    public async Task WhenGitRefusesTheRemovalAfterTakingTheUpstreamOff_ShouldAskForAReview()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "origin", Address);
        await fixture.GitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await fixture.GitAsync("branch", "-u", "origin/main", "main");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "refs", "remotes", "origin", "main.lock"), "");

        var result = await WriteAsync(fixture, expected => new RemoteRemoveRequest(expected, "origin"));

        Assert.Equal((GitWriteOutcome.Uncertain, RemoteText.NotVerified), (result.Outcome, result.Message));
        Assert.Contains("cannot lock ref 'refs/remotes/origin/main'", result.Details, StringComparison.Ordinal);
        Assert.Equal("origin", await fixture.GitAsync("remote"));
    }

    // The address as the configuration keeps it, for the dialog to change: not the shortened one the list shows.
    [Fact]
    public async Task WhenARemotesAddressIsRead_ShouldGiveItAsConfigured()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("one");
        await fixture.GitAsync("remote", "add", "upstream", "git@github.com:acme/trag.git");
        var reader = new CliBranchReader(fixture.Runner);

        Assert.Equal("git@github.com:acme/trag.git", await reader.RemoteUrlAsync(fixture.Root, "upstream", fixture.Token));
        Assert.Null(await reader.RemoteUrlAsync(fixture.Root, "missing", fixture.Token));
    }

    private static async Task<GitWriteResult> WriteAsync(GitFixture fixture, Func<Trag.Domain.Status.RepositorySnapshot, GitWriteRequest> build) =>
        await new GitWriteRouter(fixture.Runner).ExecuteAsync(build(await fixture.ReadAsync()), fixture.Token);
}
