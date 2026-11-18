using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// Every remote configuration Trag refuses to synchronize, with the exact sentence the sidebar shows, against real Git:
// the checks run in a fixed order and the later assignment wins, so the order is part of what is pinned.
public sealed class RemoteConfigurationLimitationTests
{
    private sealed record Fixture(GitFixture Git, string Bare) : IDisposable
    {
        public void Dispose() => Git.Dispose();

        public async Task<RepositorySyncState> ReadAsync()
        {
            var sync = new GitSyncFacade(Git.Runner, new CliRepositoryReader(Git.Runner));
            return await sync.ReadAsync(await Git.ReadAsync(), "origin", null, Git.Token);
        }

        public Task ConfigureAsync(params string[] arguments) => Git.GitAsync(["config", .. arguments]);

        public async Task<GitWriteResult> FetchAsync(RepositorySyncState target)
        {
            var sync = new GitSyncFacade(Git.Runner, new CliRepositoryReader(Git.Runner));
            return await sync.ExecuteAsync(new SyncRequest(await Git.ReadAsync(), target, SyncAction.Fetch), Git.Token);
        }
    }

    private static async Task<Fixture> CreateAsync()
    {
        var git = await GitFixture.CreateAsync("trunk");
        try
        {
            var bare = Path.Combine(git.Container, "origin.git");
            await git.GitAtAsync(git.Container, "init", "--bare", "--initial-branch=trunk", bare);
            await git.WriteAsync("file.txt", "one\n");
            await git.CommitAsync("first", "file.txt");
            await git.GitAsync("remote", "add", "origin", bare);
            await git.GitAsync("push", "-u", "origin", "trunk");
            return new(git, bare);
        }
        catch { git.Dispose(); throw; }
    }

    [Fact]
    public async Task WhenTheRemoteIsAnOrdinaryOne_ShouldReportNoLimitation()
    {
        using var fixture = await CreateAsync();
        var state = await fixture.ReadAsync();
        Assert.Equal("", state.Remote.Limitation);
        Assert.Equal("origin", state.Remote.Name);
    }

    // The last two cases share one sentence: an unsupported mapping next to a good one is named as such, while an
    // unsupported mapping that leaves none at all is reported by the count check, which runs later and overwrites it.
    [Theory]
    [InlineData("Mirror remotes cannot be synchronized in Trag.", new[] { "remote.origin.mirror", "true" })]
    [InlineData("Choose an OpenSSH or Plink SSH configuration before synchronizing.", new[] { "ssh.variant", "weird" })]
    [InlineData("A remote needs between 1 and 32 supported branch fetch mappings.", new[] { "--unset-all", "remote.origin.fetch" })]
    [InlineData("This remote uses an unsupported fetch mapping. Use branch mappings into its own remote-tracking namespace.", new[] { "--add", "remote.origin.fetch", "+refs/heads/*:refs/heads/*" })]
    [InlineData("A remote needs between 1 and 32 supported branch fetch mappings.", new[] { "--replace-all", "remote.origin.fetch", "+refs/heads/*:refs/heads/*" })]
    public async Task WhenTheRemoteConfigurationCannotBeSynchronized_ShouldRefuseItWithItsSentence(string sentence, string[] configuration)
    {
        using var fixture = await CreateAsync();
        await fixture.ConfigureAsync(configuration);
        Assert.Equal(sentence, (await fixture.ReadAsync()).Remote.Limitation);
    }

    // A remote whose name is another one's followed by a slash, which Git up to 2.50 adds and any Git takes from the
    // configuration, shares that remote's remote-tracking namespace: refs/remotes/origin/b/trunk is origin/b's trunk and
    // also where origin's branch b/trunk is fetched, so neither remote can tell which branches are its own.
    [Fact]
    public async Task WhenOneRemotesNameIsAnothersFollowedByASlash_ShouldRefuseToSynchronizeEither()
    {
        using var fixture = await CreateAsync();
        await fixture.ConfigureAsync("remote.origin/b.url", fixture.Bare);
        await fixture.ConfigureAsync("remote.origin/b.fetch", "+refs/heads/*:refs/remotes/origin/b/*");
        await fixture.Git.GitAsync("fetch", "origin/b");
        var sync = new GitSyncFacade(fixture.Git.Runner, new CliRepositoryReader(fixture.Git.Runner));
        var snapshot = await fixture.Git.ReadAsync();

        var origin = await sync.ReadAsync(snapshot, "origin", null, fixture.Git.Token);
        var nested = await sync.ReadAsync(snapshot, "origin/b", null, fixture.Git.Token);

        Assert.Equal("The names of remotes origin and origin/b overlap, so their remote-tracking branches mix. Rename one of them before synchronizing.",
            origin.Remote.Limitation);
        Assert.Equal("The names of remotes origin/b and origin overlap, so their remote-tracking branches mix. Rename one of them before synchronizing.",
            nested.Remote.Limitation);
    }

    // Windows keeps remote-tracking branches in folders whose names ignore case, so refs/remotes/Origin/ is the folder of
    // refs/remotes/origin/ and a fetch of either remote writes the other's branches.
    [Theory]
    [InlineData("Origin")]
    [InlineData("Origin/b")]
    public async Task WhenTwoRemotesNamesOverlapOnlyIgnoringCase_ShouldRefuseToSynchronizeEither(string other)
    {
        using var fixture = await CreateAsync();
        await fixture.ConfigureAsync("remote." + other + ".url", fixture.Bare);
        await fixture.ConfigureAsync("remote." + other + ".fetch", "+refs/heads/*:refs/remotes/" + other + "/*");
        var sync = new GitSyncFacade(fixture.Git.Runner, new CliRepositoryReader(fixture.Git.Runner));
        var snapshot = await fixture.Git.ReadAsync();

        var origin = await sync.ReadAsync(snapshot, "origin", null, fixture.Git.Token);
        var overlapping = await sync.ReadAsync(snapshot, other, null, fixture.Git.Token);

        Assert.Equal("The names of remotes origin and " + other + " overlap, so their remote-tracking branches mix. Rename one of them before synchronizing.",
            origin.Remote.Limitation);
        Assert.Equal("The names of remotes " + other + " and origin overlap, so their remote-tracking branches mix. Rename one of them before synchronizing.",
            overlapping.Remote.Limitation);
    }

    [Fact]
    public async Task WhenTheHistoryIsShallow_ShouldRefuseToSynchronize()
    {
        using var fixture = await CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Git.Root, ".git", "shallow"), "", fixture.Git.Token);
        Assert.Equal("Shallow history must be completed in Git before synchronization.", (await fixture.ReadAsync()).Remote.Limitation);
    }

    [Fact]
    public async Task WhenTheRemoteHasMoreThanThirtyTwoBranchMappings_ShouldRefuseToSynchronize()
    {
        using var fixture = await CreateAsync();
        await fixture.ConfigureAsync("--unset-all", "remote.origin.fetch");
        for (var i = 0; i < 33; i++)
            await fixture.ConfigureAsync("--add", "remote.origin.fetch", $"+refs/heads/topic-{i:D2}:refs/remotes/origin/topic-{i:D2}");
        Assert.Equal("A remote needs between 1 and 32 supported branch fetch mappings.", (await fixture.ReadAsync()).Remote.Limitation);
    }

    [Fact]
    public async Task WhenTheRemoteHasASecondFetchOrPushAddress_ShouldRefuseToSynchronize()
    {
        using var fixture = await CreateAsync();
        await fixture.ConfigureAsync("--add", "remote.origin.url", Path.Combine(fixture.Git.Container, "second.git"));
        Assert.Equal("Remotes with multiple fetch or push URLs are not supported. Choose a remote with one destination.",
            (await fixture.ReadAsync()).Remote.Limitation);
    }

    [Fact]
    public async Task WhenABranchMappingIsAdded_ShouldChangeTheConfigurationIdentity()
    {
        using var fixture = await CreateAsync();
        var first = (await fixture.ReadAsync()).Remote.ConfigurationId;
        Assert.NotEmpty(first);
        await fixture.ConfigureAsync("--add", "remote.origin.fetch", "+refs/heads/extra:refs/remotes/origin/extra");
        var second = (await fixture.ReadAsync()).Remote.ConfigurationId;
        Assert.NotEqual(first, second);
        Assert.Equal("", (await fixture.ReadAsync()).Remote.Limitation);
    }

    [Fact]
    public async Task WhenAFetchCarriesAnOlderRemoteConfigurationThanGits_ShouldRefuseItUntilItIsReadAgain()
    {
        using var fixture = await CreateAsync();
        var stale = await fixture.ReadAsync();
        Assert.Equal(GitWriteOutcome.Succeeded, (await fixture.FetchAsync(stale)).Outcome);

        // uploadpack changes nothing about where the remote is, but it changes what a selected account is bound to,
        // so the identity moves and a write that still carries the old one is refused.
        await fixture.ConfigureAsync("remote.origin.uploadpack", "git-upload-pack");
        var fresh = await fixture.ReadAsync();
        Assert.Equal(stale.Remote.FetchAddress, fresh.Remote.FetchAddress);
        Assert.Equal(stale.Remote.PushAddress, fresh.Remote.PushAddress);
        Assert.NotEqual(stale.Remote.ConfigurationId, fresh.Remote.ConfigurationId);

        var refused = await fixture.FetchAsync(stale);
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal("The remote configuration changed since it was last read. Review the synchronization target and retry.", refused.Message);
        // Reading it again is the whole remedy: the same fetch against the current configuration goes through.
        Assert.Equal(GitWriteOutcome.Succeeded, (await fixture.FetchAsync(fresh)).Outcome);
    }
}
