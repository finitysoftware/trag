using System.Text;
using Trag.TestSupport;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;

namespace Trag.IntegrationTests.Git.Sync;

public sealed class RepositoryPublishTests
{
    [Fact]
    public async Task WhenALocalOnlyRepositoryIsPublishedToANewAddress_ShouldTrackItThereAfterAVerifiedPush()
    {
        using var f = await GitFixture.CreateAsync();
        var (sync, hub, result, progress, after) = await PublishLocalOnlyToANewAddress(f);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Contains("Published fixture to origin", result.Message);
        Assert.Contains(progress, step => step.StartsWith("Pushing fixture", StringComparison.Ordinal));
        Assert.Equal("origin", Assert.Single(after.Remotes).Name); Assert.Equal("origin", after.TrackingRemote); Assert.Equal("refs/heads/fixture", after.UpstreamBranchRef);
        Assert.Equal(after.Head, await Git(hub, f.Token, "rev-parse", "refs/heads/fixture"));
        Assert.Equal(after.Head, await Git(f.Root, f.Token, "rev-parse", "refs/remotes/origin/fixture"));
        var state = await sync.ReadAsync(after, "origin", null, f.Token);
        Assert.Equal("refs/heads/fixture", state.Branch!.SourceRef); Assert.Equal(0, state.Ahead); Assert.Equal(0, state.Behind);
    }

    [Fact]
    public async Task WhenThePublishedRemoteIsAddedAgain_ShouldRefuseItAndKeepTheOneRemote()
    {
        using var f = await GitFixture.CreateAsync();
        var (sync, hub, _, _, after) = await PublishLocalOnlyToANewAddress(f);
        var again = await sync.ExecuteAsync(new PublishRequest(after, new(PublishKind.AddRemote, "origin", "fixture", true, RemoteUrl: hub)), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, again.Outcome); Assert.Contains("already exists", again.Message);
        Assert.Single((await f.ReadAsync()).Remotes);
    }

    [Fact]
    public async Task WhenARepositoryWithoutCommitsIsPublishedWithAPush_ShouldRefuseItAndAddNoRemote()
    {
        using var f = await GitFixture.CreateAsync();
        var (_, _, _, rejected) = await PublishWithoutCommitsAndPush(f);
        Assert.Equal(GitWriteOutcome.Rejected, rejected.Outcome); Assert.Contains("commit", rejected.Message); Assert.Empty((await f.ReadAsync()).Remotes);
    }

    [Fact]
    public async Task WhenARepositoryWithoutCommitsIsPublishedWithoutAPush_ShouldOnlyAddTheRemote()
    {
        using var f = await GitFixture.CreateAsync();
        var (sync, hub, snapshot, _) = await PublishWithoutCommitsAndPush(f);
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.AddRemote, "origin", "fixture", false, RemoteUrl: hub)), f.Token);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details); Assert.Contains("Use Push", result.Message);
        var after = await f.ReadAsync();
        Assert.Equal("origin", Assert.Single(after.Remotes).Name); Assert.Equal("origin", after.TrackingRemote);
        Assert.Equal("", await Git(hub, f.Token, "for-each-ref"));
    }

    // The sync state offers the branch under its own name first, marked as new, next to the branches the remote already has.
    [Fact]
    public async Task WhenABranchHasNoUpstream_ShouldOfferItAsANewRemoteBranch()
    {
        using var f = await SyncFixture.Create();
        var (_, offered) = await UnpublishedTopic(f);
        Assert.True(offered.Branch!.IsNew); Assert.Equal("refs/heads/topic", offered.Branch.SourceRef); Assert.Same(offered.Branch, offered.Branches[0]); Assert.Null(offered.Ahead);
        Assert.Contains(offered.Branches, item => item.Display == "trunk" && !item.IsNew); Assert.Contains("not on origin yet", offered.Message);
    }

    [Fact]
    public async Task WhenABranchIsPublishedToARemoteThatNoLongerExists_ShouldRefuseIt()
    {
        using var f = await SyncFixture.Create();
        var (_, missing) = await TopicPublishedToAMissingRemote(f);
        Assert.Equal(GitWriteOutcome.Rejected, missing.Outcome); Assert.Contains("no longer exists", missing.Message);
    }

    [Fact]
    public async Task WhenABranchIsPublishedUnderAnInvalidRemoteBranchName_ShouldRefuseItAndRecordNoUpstream()
    {
        using var f = await SyncFixture.Create();
        var (_, badName) = await TopicPublishedUnderABadName(f);
        Assert.Equal(GitWriteOutcome.Rejected, badName.Outcome); Assert.Null((await f.Reader.ReadAsync(f.One, f.Token)).TrackingRemote);
    }

    [Fact]
    public async Task WhenABranchWithoutUpstreamIsPublishedToTheExistingRemote_ShouldTrackItUnderTheChosenNameWithoutAddingARemote()
    {
        using var f = await SyncFixture.Create();
        var (snapshot, _) = await TopicPublishedUnderABadName(f);
        var result = await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true, RemoteBranch: "shared/topic")), f.Token);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details); Assert.Contains("origin/shared/topic", result.Message);
        var after = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal("origin", after.TrackingRemote); Assert.Equal("refs/heads/shared/topic", after.UpstreamBranchRef);
        Assert.Equal(after.Head, await f.Git(f.Bare, "rev-parse", "refs/heads/shared/topic"));
        Assert.Equal(after.Head, await f.Git(f.One, "rev-parse", "refs/remotes/origin/shared/topic"));
        Assert.Equal("origin", await f.Git(f.One, "remote"));
        var state = await f.Sync.ReadAsync(after, "origin", null, f.Token); Assert.Equal("refs/heads/shared/topic", state.Branch!.SourceRef); Assert.Equal(0, state.Ahead);
        Assert.Contains(state.Branches, item => item.Display == "topic" && item.IsNew);
    }

    // The tracking ref is the one the remote's fetch mapping names, so the next fetch moves it; a remote that fetches only
    // some branches gets none for a branch no fetch brings, rather than one no fetch would ever update.
    [Theory]
    [InlineData("+refs/heads/*:refs/remotes/origin/feature/*", "refs/remotes/origin/feature/topic")]
    [InlineData("+refs/heads/trunk:refs/remotes/origin/trunk", null)]
    public async Task WhenTheFetchMappingRenamesOrLeavesOutTheBranch_ShouldRecordTheTrackingRefItNamesOrNone(string mapping, string? tracking)
    {
        using var f = await SyncFixture.Create();
        var (snapshot, _) = await UnpublishedTopic(f);
        await f.Git(f.One, "config", "remote.origin.fetch", mapping);

        var result = await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain("refs/remotes/origin/topic", (await f.Git(f.One, "for-each-ref", "--format=%(refname)", "refs/remotes")).Split('\n'));
        if (tracking is not null) Assert.Equal(snapshot.Head, await f.Git(f.One, "rev-parse", tracking));
    }

    // A branch the remote already has takes the publish only as a fast-forward: Git refuses any other, the remote keeps its
    // branch, and that is a refusal, not a remote that changed under the push.
    [Fact]
    public async Task WhenTheExistingRemoteBranchDoesNotFastForward_ShouldRejectThePublish()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "elsewhere"); await f.Commit(f.One, "elsewhere work");
        await f.Git(f.One, "push", "origin", "elsewhere:shared");
        await f.Git(f.One, "switch", "-");
        var shared = await f.Git(f.Bare, "rev-parse", "refs/heads/shared");
        var (snapshot, _) = await UnpublishedTopic(f);

        var result = await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true, RemoteBranch: "shared")), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Rejected, result.Outcome + ": " + result.Message + result.Details);
        Assert.Equal(shared, await f.Git(f.Bare, "rev-parse", "refs/heads/shared"));
    }

    [Fact]
    public async Task WhenThePushOfAPublishIsRejected_ShouldKeepTheRemoteAndUpstreamSoAPushCanRetry()
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        var hub = await Bare(f, "hub.git");
        var hooks = Path.Combine(f.Container, "hub-hooks"); Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-receive");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\necho 'Fixture rejects push' >&2\nexit 1\n", new UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Git(hub, f.Token, "config", "core.hooksPath", hooks);
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()));
        var snapshot = await f.ReadAsync();
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.AddRemote, "origin", "fixture", true, RemoteUrl: hub)), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Contains("origin was added", result.Message); Assert.Contains("Use Push to retry", result.Message); Assert.Contains("Fixture rejects push", result.Details);
        var after = await f.ReadAsync();
        Assert.Equal("origin", Assert.Single(after.Remotes).Name); Assert.Equal("origin", after.TrackingRemote); Assert.Equal("", await Git(hub, f.Token, "for-each-ref"));
        await Git(hub, f.Token, "config", "--unset", "core.hooksPath");
        var state = await sync.ReadAsync(after, "origin", null, f.Token); Assert.Equal("refs/heads/fixture", state.Branch!.SourceRef);
        var push = await sync.ExecuteAsync(new SyncRequest(after, state, SyncAction.Push), f.Token);
        Assert.True(push.Outcome == GitWriteOutcome.Succeeded, push.Message + push.Details);
        Assert.Equal(after.Head, await Git(hub, f.Token, "rev-parse", "refs/heads/fixture"));
    }

    // A host that has no repository at the address took nothing, so there is nothing to verify: the publish is refused
    // and says the repository is missing.
    [Fact]
    public async Task WhenABranchIsPublishedToAnAddressItsHostHasNoRepositoryAt_ShouldRefuseItAsAMissingRepository()
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        using var host = new LoopbackGitServer(Path.Combine(f.Container, "served"));
        await f.GitAsync("remote", "add", "hub", host.Url("nadia/sample-app"));
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()));
        var snapshot = await f.ReadAsync();
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "hub", "fixture", true)), f.Token);
        Assert.True(result is { Outcome: GitWriteOutcome.Rejected, MissingRepository: true }, result.Outcome + " " + result.Message + result.Details);
        Assert.Contains("The host has no repository at this remote's address", result.Message);
    }

    // The repository the existing remote's address names is created from the account first; the remote stays as it was.
    [Fact]
    public async Task WhenTheMissingRepositoryOfAnExistingRemoteIsCreated_ShouldCreateItFromTheAccountThenPublishTheBranchThere()
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        var hub = await Bare(f, "hub.git");
        await f.GitAsync("remote", "add", "hub", hub);
        var accounts = new Creator();
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()), accounts: accounts);
        var snapshot = await f.ReadAsync();
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "hub", "fixture", true, AccountId: "account",
            RepositoryName: "sample-app", IsPrivate: true)), f.Token);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Published fixture to nadia/sample-app. hub/fixture is now its upstream.", result.Message);
        Assert.Equal([("account", (string?)null, "sample-app", true)], accounts.Created);
        Assert.Equal(snapshot.Head, await Git(hub, f.Token, "rev-parse", "refs/heads/fixture"));
        Assert.Equal(hub, await Git(f.Root, f.Token, "remote", "get-url", "hub"));
    }

    // Cancel arrives once the provider has created the repository and before its remote is added: there is nothing to push
    // to yet, so the message names the repository and says which address to add as which remote.
    [Fact]
    public async Task WhenPublishIsCanceledAfterCreateBeforeRemoteAdd_ShouldNameTheCreatedRepositoryInTheMessage()
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()), accounts: new Creator { AfterCreate = cancel.Cancel });

        var result = await sync.ExecuteAsync(new PublishRequest(await f.ReadAsync(), new(PublishKind.CreateRepository, "origin", "fixture", true,
            AccountId: "account", RepositoryName: "trag", IsPrivate: true)), cancel.Token);

        Assert.Equal(GitWriteOutcome.Cancelled, result.Outcome);
        Assert.Equal(PublishOutcomeText.CreatedOnProvider("nadia/trag") + " "
            + PublishOutcomeText.CanceledBeforeRemote("https://github.com/nadia/trag.git", "origin"), result.Message);
        Assert.Empty((await f.ReadAsync()).Remotes);
    }

    // The account chosen for the new remote cannot be remembered, as when another program holds the repository's
    // configuration lock: the publish stops and says so, instead of pushing with whatever the remote falls back to.
    [Fact]
    public async Task WhenSavingTheAccountSelectionFails_ShouldSaySoAndNotPush()
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        var locked = Path.Combine(f.Root, ".git", "config.lock");
        var accounts = new Creator { Listed = [Account("github:1"), Account("github:2")], OnAccounts = () => File.WriteAllText(locked, "") };
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()), accounts: accounts);

        var result = await sync.ExecuteAsync(new PublishRequest(await f.ReadAsync(), new(PublishKind.AddRemote, "origin", "fixture", true,
            RemoteUrl: "https://127.0.0.1:9/team/repo.git", AccountId: "github:2")), f.Token);
        File.Delete(locked);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal("Remote origin was added. fixture now tracks origin/fixture. " + AccountFailureText.SelectionNotSaved, result.Message);
    }

    private static AccountInfo Account(string id) =>
        new(id, AccountProvider.GitHub, id, id, new("https://127.0.0.1:9/api/"), new("https://127.0.0.1:9/"), true, DateTimeOffset.UtcNow, null);

    [Theory]
    [InlineData("bad name", "hub")]
    [InlineData("-origin", "hub")]
    [InlineData("", "hub")]
    [InlineData("origin", "")]
    [InlineData("origin", "-c core.pager=evil")]
    public async Task WhenTheRemoteNameOrAddressIsInvalid_ShouldRefuseThePublishAndChangeNothing(string remote, string address)
    {
        using var f = await GitFixture.CreateAsync();
        await Commit(f, "first");
        var hub = await Bare(f, "hub.git");
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()));
        var snapshot = await f.ReadAsync();
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.AddRemote, remote, "fixture", true, RemoteUrl: address == "hub" ? hub : address)), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        var after = await f.ReadAsync(); Assert.Empty(after.Remotes); Assert.Null(after.TrackingRemote);
        Assert.Equal("", await Git(hub, f.Token, "for-each-ref"));
    }

    // A repository with one commit and no remote, published to a new bare address with its upstream, then read back.
    private static async Task<(GitSyncFacade Sync, string Hub, GitWriteResult Result, List<string> Progress, RepositorySnapshot After)>
        PublishLocalOnlyToANewAddress(GitFixture f)
    {
        await Commit(f, "first");
        var hub = await Bare(f, "hub.git");
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()));
        var snapshot = await f.ReadAsync(); Assert.Empty(snapshot.Remotes); Assert.Null(snapshot.TrackingRemote);
        var progress = new List<string>();
        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.AddRemote, "origin", "fixture", true, RemoteUrl: hub))
            { Progress = new Collector(progress) }, f.Token);
        return (sync, hub, result, progress, await f.ReadAsync());
    }

    // An unborn repository asked to publish to a new address and push.
    private static async Task<(GitSyncFacade Sync, string Hub, RepositorySnapshot Snapshot, GitWriteResult Rejected)> PublishWithoutCommitsAndPush(GitFixture f)
    {
        var hub = await Bare(f, "hub.git");
        var sync = new GitSyncFacade(new(), new CliRepositoryReader(new()));
        var snapshot = await f.ReadAsync(); Assert.Null(snapshot.Head);
        var rejected = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.AddRemote, "origin", "fixture", true, RemoteUrl: hub)), f.Token);
        return (sync, hub, snapshot, rejected);
    }

    // A topic branch with a commit origin has never seen, and the sync state read for it.
    private static async Task<(RepositorySnapshot Snapshot, RepositorySyncState Offered)> UnpublishedTopic(SyncFixture f)
    {
        await f.Git(f.One, "switch", "--no-track", "-c", "topic"); await f.Commit(f.One, "topic work");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token); Assert.Null(snapshot.TrackingRemote);
        return (snapshot, await f.Sync.ReadAsync(snapshot, "origin", null, f.Token));
    }

    private static async Task<(RepositorySnapshot Snapshot, GitWriteResult Missing)> TopicPublishedToAMissingRemote(SyncFixture f)
    {
        var (snapshot, _) = await UnpublishedTopic(f);
        return (snapshot, await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "nowhere", "topic", true)), f.Token));
    }

    private static async Task<(RepositorySnapshot Snapshot, GitWriteResult BadName)> TopicPublishedUnderABadName(SyncFixture f)
    {
        var (snapshot, _) = await TopicPublishedToAMissingRemote(f);
        return (snapshot, await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true, RemoteBranch: "bad name")), f.Token));
    }

    private static async Task Commit(GitFixture f, string text)
    {
        await File.WriteAllTextAsync(Path.Combine(f.Root, "shared.txt"), text + "\n", f.Token);
        await f.GitAsync("add", "shared.txt"); await f.GitAsync("commit", "-m", text);
    }
    private static async Task<string> Bare(GitFixture f, string name)
    {
        var path = Path.Combine(f.Container, name);
        await Git(f.Container, f.Token, "init", "--bare", "--initial-branch=fixture", path);
        return path;
    }
    private static async Task<string> Git(string root, CancellationToken ct, params string[] args)
    {
        var result = await new GitProcessRunner().RunAsync(root, args, ct);
        Assert.True(result.ExitCode == 0, Encoding.UTF8.GetString(result.StandardError));
        return Encoding.UTF8.GetString(result.StandardOutput).Trim();
    }
    private sealed class Collector(List<string> steps) : IProgress<string> { public void Report(string value) { lock (steps) steps.Add(value); } }

    // The provider as publish sees it: it records each repository it is asked to create.
    private sealed class Creator : IGitAccounts
    {
        public List<(string AccountId, string? OwnerId, string Name, bool IsPrivate)> Created { get; } = [];
        // Runs once the repository is created, as a Cancel that arrives right then would.
        public Action? AfterCreate { get; init; }
        public IReadOnlyList<AccountInfo> Listed { get; init; } = [];
        // Runs whenever the accounts are listed, as the repository's account selection is read or saved.
        public Action? OnAccounts { get; init; }

        public Task<RemoteRepository> CreateRepositoryAsync(string accountId, string? ownerId, string name, bool isPrivate, CancellationToken ct)
        {
            Created.Add((accountId, ownerId, name, isPrivate));
            AfterCreate?.Invoke();
            return Task.FromResult(new RemoteRepository("nadia/" + name, "https://github.com/nadia/" + name + ".git", "https://github.com/nadia/" + name, null, isPrivate, null));
        }

        public Task<CredentialBroker> CreateBrokerAsync(string accountId, Uri endpoint, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct)
        {
            OnAccounts?.Invoke();
            return Task.FromResult(Listed);
        }
        public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct) => Task.FromResult(new RemoteRepositoryList([], ReachedLimit: false));
        public Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PublishOwner>>([]);
    }
}
