using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// What the remote itself says a branch points at, read from its push endpoint.
public sealed class GitTransportTests
{
    // The push endpoint is the remote's own URL, so Git is asked by the remote's name, with no alias to verify first.
    [Fact]
    public async Task WhenThePushEndpointIsTheRemotesOwnUrl_ShouldReadTheTipWithOneGitProcess()
    {
        using var fixture = await GitFixture.CreateAsync();
        var tip = await fixture.CommitAsync("initial");
        var bare = Path.Combine(fixture.Container, "remote.git");
        await fixture.GitAsync("init", "--bare", bare);
        await fixture.GitAsync("remote", "add", "origin", bare);
        await fixture.GitAsync("push", "origin", "fixture");
        var processes = 0;
        var runner = new GitProcessRunner(exited: _ => Interlocked.Increment(ref processes));
        var config = await new RemoteConfigurationReader(new PresetGitRunner(runner, LocalGitPreset.Writing), new GitRefNameValidator(runner))
            .ReadConfigurationAsync(await fixture.ReadAsync(), "origin", fixture.Token);
        processes = 0;

        var read = await new GitTransport(runner, null, null, null, SyncTimeouts.Quiet).RemoteTipAsync(fixture.Root, config, "refs/heads/fixture", fixture.Token);

        Assert.Equal((tip, 1), (read, processes));
    }

    // A fetch with a Trag account goes to a process-only alias of the remote, and the alias fetches as the remote would:
    // one set to fetch no tags brings none. Without an HTTPS host an account cannot reach the alias, but a push address of
    // its own leads to the same alias, so the fetch here goes to the push endpoint.
    [Fact]
    public async Task WhenAFetchGoesThroughTheVerifiedAliasOfARemoteThatFetchesNoTags_ShouldBringNoTags()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.CommitAsync("initial");
        var bare = Path.Combine(fixture.Container, "remote.git");
        await fixture.GitAsync("init", "--bare", bare);
        await fixture.GitAsync("remote", "add", "origin", bare);
        await fixture.GitAsync("tag", "v1");
        await fixture.GitAsync("push", "origin", "fixture", "v1");
        await fixture.GitAsync("tag", "--delete", "v1");
        await fixture.GitAsync("config", "remote.origin.pushurl", bare.Replace('\\', '/'));
        await fixture.GitAsync("config", "remote.origin.tagOpt", "--no-tags");
        var runner = new GitProcessRunner();
        var config = await new RemoteConfigurationReader(new PresetGitRunner(runner, LocalGitPreset.Writing), new GitRefNameValidator(runner))
            .ReadConfigurationAsync(await fixture.ReadAsync(), "origin", fixture.Token);

        var fetch = await new GitTransport(runner, null, null, null, SyncTimeouts.Quiet).TransportAsync(fixture.Root, config, "fetch",
            ["--no-write-fetch-head"], ["+refs/heads/fixture:refs/remotes/origin/fixture"], fixture.Token, pushEndpoint: true);

        Assert.Equal((0, ""), (fetch.ExitCode, await fixture.GitAsync("tag", "--list")));
    }

    // The alias prunes as the remote would: one set to prune takes away the tracking branch of a branch deleted on it.
    [Fact]
    public async Task WhenAFetchGoesThroughTheVerifiedAliasOfARemoteThatPrunes_ShouldRemoveTheTrackingBranchOfADeletedBranch()
    {
        using var fixture = await GitFixture.CreateAsync();
        var tip = await fixture.CommitAsync("initial");
        var bare = Path.Combine(fixture.Container, "remote.git");
        await fixture.GitAsync("init", "--bare", bare);
        await fixture.GitAsync("remote", "add", "origin", bare);
        await fixture.GitAsync("push", "origin", "fixture");
        await fixture.GitAsync("update-ref", "refs/remotes/origin/gone", tip);
        await fixture.GitAsync("config", "remote.origin.pushurl", bare.Replace('\\', '/'));
        await fixture.GitAsync("config", "remote.origin.prune", "true");
        var runner = new GitProcessRunner();
        var config = await new RemoteConfigurationReader(new PresetGitRunner(runner, LocalGitPreset.Writing), new GitRefNameValidator(runner))
            .ReadConfigurationAsync(await fixture.ReadAsync(), "origin", fixture.Token);

        var fetch = await new GitTransport(runner, null, null, null, SyncTimeouts.Quiet).TransportAsync(fixture.Root, config, "fetch",
            ["--no-write-fetch-head", "--refmap="], ["+refs/heads/*:refs/remotes/origin/*"], fixture.Token, pushEndpoint: true);

        Assert.Equal((0, ""), (fetch.ExitCode, await fixture.GitAsync("for-each-ref", "refs/remotes/origin/gone")));
    }
}
