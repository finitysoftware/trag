using System.Net;
using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.Infrastructure.Platform.Secrets;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Accounts;

public sealed class AccountMappingTests
{
    [Fact]
    public async Task WhenEachRepositorySavedItsOwnAccount_ShouldSelectItAgainAfterARestartWithoutStoringTheToken()
    {
        using var one = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); using var two = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2));
        var vault = new FakeSecretStore(); var path = Path.Combine(one.Container, "accounts.json");
        using var accounts = new AccountService(path, vault, new(new IdentityHandler()));
        var (alice, bob, runner, reader, _) = await SaveAliceForOneAndBobForTwo(one, two, accounts);
        using var restarted = new AccountService(path, vault, new(new IdentityHandler()));
        var restartSync = new GitSyncFacade(runner, reader, accounts: restarted);
        Assert.Equal(alice.Id, (await restartSync.ReadAsync(await one.ReadAsync(), "origin", null, one.Token)).Accounts!.SelectedId);
        Assert.Equal(bob.Id, (await restartSync.ReadAsync(await two.ReadAsync(), "origin", null, two.Token)).Accounts!.SelectedId);
        var saved = await one.RunAsync("config", "--local", "--get-regexp", "^trag-accounts\\.");
        Assert.EndsWith(alice.Id, Encoding.UTF8.GetString(saved.StandardOutput).Trim());
        Assert.DoesNotContain("alice-token", await File.ReadAllTextAsync(Path.Combine(one.Root, ".git", "config")));
    }
    [Fact]
    public async Task WhenThePushAddressMovesToAnotherHostAfterAnAccountWasSaved_ShouldUseGitsOwnCredentials()
    {
        using var one = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); using var two = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2));
        var vault = new FakeSecretStore(); var path = Path.Combine(one.Container, "accounts.json");
        using var accounts = new AccountService(path, vault, new(new IdentityHandler()));
        var (_, _, _, _, sync) = await SaveAliceForOneAndBobForTwo(one, two, accounts);
        async Task<RepositorySyncState> State(GitFixture f) => await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        async Task Select(GitFixture f, string id)
        {
            var result = await sync.ExecuteAsync(new SyncRequest(await f.ReadAsync(), await State(f), SyncAction.SelectAccount, id), f.Token);
            Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        }
        var previous = await State(one);
        await one.GitAsync("remote", "set-url", "--push", "origin", "https://other.example/project.git");
        Assert.Equal(AccountChoice.External, (await State(one)).Accounts!.SelectedId);
        Assert.Single((await State(one)).Accounts!.Choices); // Only external Git is valid across distinct HTTPS hosts.
        var result = await sync.ExecuteAsync(new SyncRequest(await one.ReadAsync(), previous, SyncAction.Fetch), one.Token);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        await Select(one, AccountChoice.External);
    }
    [Fact]
    public async Task WhenTheSavedAccountIsRemoved_ShouldUseGitsOwnCredentialsAgain()
    {
        using var one = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); using var two = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2));
        var vault = new FakeSecretStore(); var path = Path.Combine(one.Container, "accounts.json");
        using var accounts = new AccountService(path, vault, new(new IdentityHandler()));
        var (_, bob, runner, reader, sync) = await SaveAliceForOneAndBobForTwo(one, two, accounts);
        using var restarted = new AccountService(path, vault, new(new IdentityHandler()));
        var restartSync = new GitSyncFacade(runner, reader, accounts: restarted);
        await MoveOnesPushAddressAndUseGitsOwnCredentials(one, sync);
        await restarted.RemoveAsync(bob.Id, one.Token);
        Assert.Equal(AccountChoice.External, (await restartSync.ReadAsync(await two.ReadAsync(), "origin", null, two.Token)).Accounts!.SelectedId);
    }
    [Fact]
    public async Task WhenOnlyOneAccountMatches_ShouldUseItByDefaultUntilTheRepositoryChoosesOtherwise()
    {
        using var f = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); var vault = new FakeSecretStore();
        using var accounts = new AccountService(Path.Combine(f.Container, "accounts.json"), vault, new(new IdentityHandler()));
        var alice = await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, f.Token);
        await f.GitAsync("remote", "add", "origin", "https://gitlab.example:8443/team/project.git");
        var runner = new GitProcessRunner(); var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader, accounts: accounts);
        async Task<RepositorySyncState> State() => await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);

        // Nothing saved, one matching account: it is the default and the message says so.
        var state = await State();
        Assert.Equal(alice.Id, state.Accounts!.SelectedId); Assert.Contains("only account matching", state.Accounts.Message); Assert.Equal(2, state.Accounts.Choices.Count);
        Assert.Empty((await f.RunAsync("config", "--local", "--get-regexp", "^trag-accounts\\.")).StandardOutput);

        // Choosing Git's own credentials is remembered, even after a second matching account appears.
        var external = await sync.ExecuteAsync(new SyncRequest(await f.ReadAsync(), state, SyncAction.SelectAccount, AccountChoice.External), f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, external.Outcome);
        Assert.Equal(AccountChoice.External, (await State()).Accounts!.SelectedId);
        await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "bob-token"u8.ToArray(), false, f.Token);
        Assert.Equal(AccountChoice.External, (await State()).Accounts!.SelectedId); Assert.Equal(3, (await State()).Accounts!.Choices.Count);
    }

    // The choice belongs to this remote and its addresses: another remote, or a rewrite rule that leaves this remote's
    // addresses as they were, does not take it away.
    [Fact]
    public async Task WhenAnotherRemoteIsAdded_ShouldKeepGitsOwnCredentialsChosenForThisOne()
    {
        using var f = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); var vault = new FakeSecretStore();
        using var accounts = new AccountService(Path.Combine(f.Container, "accounts.json"), vault, new(new IdentityHandler()));
        await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, f.Token);
        await f.GitAsync("remote", "add", "origin", "https://gitlab.example:8443/team/project.git");
        var runner = new GitProcessRunner();
        var sync = new GitSyncFacade(runner, new CliRepositoryReader(runner), accounts: accounts);
        async Task<RepositorySyncState> State() => await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        var external = await sync.ExecuteAsync(new SyncRequest(await f.ReadAsync(), await State(), SyncAction.SelectAccount, AccountChoice.External), f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, external.Outcome);

        await f.GitAsync("remote", "add", "upstream", "https://elsewhere.example/team/project.git");
        await f.GitAsync("config", "url.https://mirror.example/.insteadOf", "https://unrelated.example/");

        Assert.Equal(AccountChoice.External, (await State()).Accounts!.SelectedId);
    }

    // The choice is kept under the remote's name, which Git's remote rename and remove leave alone: Trag takes it with the
    // remote, so no key stays that nothing reads, and a remote added later under that name and address starts over. A
    // removal Git refuses, here at a remote-tracking ref another program locks, keeps the remote and its choice.
    [Theory]
    [InlineData("rename")]
    [InlineData("remove")]
    [InlineData("refused")]
    public async Task WhenTheRemoteIsRenamedOrRemoved_ShouldRemoveItsSavedChoice(string change)
    {
        using var f = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2)); var vault = new FakeSecretStore();
        using var accounts = new AccountService(Path.Combine(f.Container, "accounts.json"), vault, new(new IdentityHandler()));
        await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, f.Token);
        await f.GitAsync("remote", "add", "origin", "https://gitlab.example:8443/team/project.git");
        var sync = new GitSyncFacade(f.Runner, new CliRepositoryReader(f.Runner), accounts: accounts);
        var state = await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, (await sync.ExecuteAsync(new SyncRequest(await f.ReadAsync(), state, SyncAction.SelectAccount, AccountChoice.External), f.Token)).Outcome);
        var saved = (await f.RunAsync("config", "--local", "--get-regexp", "^trag-accounts\\.")).StandardOutput;
        Assert.NotEmpty(saved);
        if (change == "refused")
        {
            await f.CommitAsync("one");
            await f.GitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
            await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "refs", "remotes", "origin", "main.lock"), "", f.Token);
        }
        var expected = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(
            change == "rename" ? new RemoteEditRequest(expected, "origin", "upstream", null) : new RemoteRemoveRequest(expected, "origin"), f.Token);

        Assert.True(result.Outcome == (change == "refused" ? GitWriteOutcome.Uncertain : GitWriteOutcome.Succeeded), result.Message + " " + result.Details);
        Assert.Equal(change == "refused" ? saved : [], (await f.RunAsync("config", "--local", "--get-regexp", "^trag-accounts\\.")).StandardOutput);
    }

    [Fact]
    public async Task WhenTheRemoteIsAnSshAlias_ShouldUseItsExistingKeysAndNeverCreateATokenBroker()
    {
        using var f = await GitFixture.CreateAsync(); var vault = new FakeSecretStore();
        using var accounts = new AccountService(Path.Combine(f.Container, "accounts.json"), vault, new(new IdentityHandler()));
        var alice = await accounts.SignInGitLabAsync("https://gitlab.example", "alice-token"u8.ToArray(), false, f.Token);
        await f.GitAsync("remote", "add", "origin", "git@work-alias:team/project.git");
        var runner = new GitProcessRunner(); var sync = new GitSyncFacade(runner, new CliRepositoryReader(runner), accounts: accounts);
        var snapshot = await f.ReadAsync(); var state = await sync.ReadAsync(snapshot, "origin", null, f.Token);
        Assert.Equal(AccountChoice.External, state.Accounts!.SelectedId);
        Assert.Equal(GitWriteOutcome.Succeeded, (await sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.SelectAccount, alice.Id), f.Token)).Outcome);
        Assert.Contains("configured key", (await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token)).Accounts!.Message);
        await f.GitAsync("remote", "set-url", "origin", "git@personal-alias:team/project.git");
        Assert.Equal(AccountChoice.External, (await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token)).Accounts!.SelectedId);
        await Assert.ThrowsAsync<AccountException>(() => accounts.CreateBrokerAsync(alice.Id, new("ssh://work-alias/team/project.git"), f.Token));
    }
    // The account chosen for a remote is checked once more right before Git pushes, so one changed after the destination
    // was read stops the push before Git starts: nothing was sent, and nothing waits for a review. The SSH command
    // is a script that runs Git's own programs on the local bare remote and changes the choice while it answers that read.
    [Fact]
    public async Task WhenTheAccountChangesDuringAPush_ShouldSayNothingWasSentNotUncertain()
    {
        using var f = await SyncFixture.Create(); await f.Commit(f.One, "mine");
        using var accounts = new AccountService(Path.Combine(f.Root, "accounts.json"), new FakeSecretStore(), new(new IdentityHandler()));
        var alice = await accounts.SignInGitLabAsync("https://gitlab.example", "alice-token"u8.ToArray(), false, f.Token);
        var script = Path.Combine(f.Root, "fake-ssh.sh"); var one = SyncFixture.Quote(f.One); var bare = SyncFixture.Quote(f.Bare);
        await File.WriteAllTextAsync(script, "#!/bin/sh\nfor last; do :; done\ncase \"$last\" in\n"
            + "git-upload-pack*) git -C " + one + " config --local --replace-all \"$(git -C " + one + " config --local --name-only --get-regexp '^trag-accounts[.]')\" gone\n"
            + "  exec git-upload-pack " + bare + " ;;\n*) exec git-receive-pack " + bare + " ;;\nesac\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.sshCommand", "sh " + SyncFixture.Quote(script));
        await f.Git(f.One, "remote", "set-url", "origin", "git@gitlab.example:team/project.git");
        var runner = new GitProcessRunner(); var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader, accounts: accounts);
        var snapshot = await reader.ReadAsync(f.One, f.Token);
        var selected = await sync.ExecuteAsync(new SyncRequest(snapshot, await sync.ReadAsync(snapshot, "origin", null, f.Token), SyncAction.SelectAccount, alice.Id), f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, selected.Outcome);
        var before = await f.Head(f.Bare);

        snapshot = await reader.ReadAsync(f.One, f.Token);
        var result = await sync.ExecuteAsync(new SyncRequest(snapshot, await sync.ReadAsync(snapshot, "origin", null, f.Token), SyncAction.Push), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.PushNotStarted(SyncOutcomeText.AccountChangedDuringSync)), (result.Outcome, result.Message));
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // Which accounts exist is the list Trag keeps; whether the OS credential store is unlocked matters only to signing in,
    // so reading a remote's account, before every sync and for every broker, never asks it.
    [Fact]
    public async Task WhenARemotesAccountIsRead_ShouldNotAskTheCredentialStoreItsStatus()
    {
        using var f = await GitFixture.CreateAsync(); var vault = new FakeSecretStore();
        using var accounts = new AccountService(null, vault, new(new IdentityHandler()));
        var store = new RemoteAccountStore(new(), accounts);
        await store.ReadAsync(f.Root, "origin", "https://example.invalid/o.git", "https://example.invalid/o.git", f.Token);
        Assert.Empty(await accounts.AccountsAsync(f.Token));
        Assert.Equal(0, vault.StatusReads);
    }

    [Fact]
    public async Task WhenGitsConfigIsLocked_ShouldNotChangeTheAccountSelection()
    {
        using var f = await GitFixture.CreateAsync(); var vault = new FakeSecretStore();
        using var accounts = new AccountService(null, vault, new(new IdentityHandler()));
        var store = new RemoteAccountStore(new(), accounts);
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "config.lock"), "foreign lock", f.Token);
        await Assert.ThrowsAsync<AccountException>(() => store.SaveAsync(f.Root, "origin", "local-path", "local-path", AccountChoice.External, f.Token));
        Assert.Equal("foreign lock", await File.ReadAllTextAsync(Path.Combine(f.Root, ".git", "config.lock")));
    }
    [Fact]
    public async Task WhenAnAccountIsKeptInTheWindowsVault_ShouldSurviveARestartAndRemoveOnlyItsOwnSecret()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await GitFixture.CreateAsync(); var vault = new WindowsSecretStore();
        var path = Path.Combine(f.Container, "accounts.json");
        using var accounts = new AccountService(path, vault, new(new IdentityHandler()));
        var origin = "https://" + Guid.NewGuid().ToString("N") + ".example.invalid";
        var id = AccountIdentity.StableId(AccountProvider.GitLab, AccountMapping.GitLabApiBase(origin), "1");
        try
        {
            var alice = await accounts.SignInGitLabAsync(origin, "alice-token"u8.ToArray(), false, f.Token);
            using var restarted = new AccountService(path, vault, new(new IdentityHandler()));
            Assert.Equal(alice.Id, (await restarted.VerifyAsync(alice.Id, f.Token)).Id);
            await restarted.RemoveAsync(alice.Id, f.Token); Assert.Null(await vault.ReadAsync(alice.Id, f.Token));
        }
        finally { await vault.DeleteAsync(id, CancellationToken.None); }
    }
    // GitHub refuses to renew the expired token of the one account for this remote: the fetch never reaches Git, says to
    // sign in again, and keeps GitHub's answer in its details without anything that looks like a secret.
    [Fact]
    public async Task WhenGitHubRefusesToRenewTheToken_ShouldAskToSignInAgainAndKeepGitHubsAnswerInTheDetails()
    {
        using var one = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2));
        using var accounts = new AccountService(Path.Combine(one.Container, "accounts.json"), new FakeSecretStore(), new(new RefusingRenewal(), new FastClock()));
        await accounts.SignInGitHubAsync(false, new Progress<DeviceLoginProgress>(), one.Token);
        await one.GitAsync("remote", "add", "origin", "https://github.com/team/project.git");
        var runner = new GitProcessRunner();
        // Any helper path will do: the renewal is refused before a broker exists, so Git never starts.
        var sync = new GitSyncFacade(runner, new CliRepositoryReader(runner), accounts: accounts, askPassExecutable: Environment.ProcessPath);
        var state = await sync.ReadAsync(await one.ReadAsync(), "origin", null, one.Token);
        var result = await sync.ExecuteAsync(new SyncRequest(await one.ReadAsync(), state, SyncAction.Fetch), one.Token);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(SignInText.NotRenewable, result.Message);
        Assert.Contains("incorrect_client_credentials", result.Details);
        Assert.DoesNotContain("do-not-display", result.Details);
    }

    // The Git LFS content a merge downloads goes through the remote's account like any transfer: GitHub refusing to renew
    // its token stops the download before Git. The LFS storage is a local path, so nothing reaches the network.
    [Fact]
    public async Task WhenAnLfsDownloadForAMergeUsesAnAccountGitHubRefusesToRenew_ShouldAskToSignInAgainBeforeGit()
    {
        using var one = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(2));
        using var accounts = new AccountService(Path.Combine(one.Container, "accounts.json"), new FakeSecretStore(), new(new RefusingRenewal(), new FastClock()));
        await accounts.SignInGitHubAsync(false, new Progress<DeviceLoginProgress>(), one.Token);
        await LfsConfig.SetAsync(one, one.Root, installed: true);
        await one.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await one.WriteAsync("asset.bin", "asset\n");
        var head = await one.CommitAsync("Asset", ".gitattributes", "asset.bin");
        await one.GitAsync("remote", "add", "origin", "https://github.com/team/project.git");
        await one.GitAsync("config", "lfs.url", new Uri(Path.Combine(one.Container, "lfs-store")).AbsoluteUri);
        var runner = new GitProcessRunner();
        var sync = new GitSyncFacade(runner, new CliRepositoryReader(runner), accounts: accounts, askPassExecutable: Environment.ProcessPath);

        var result = await sync.ExecuteAsync(new LfsDownloadRequest(await one.ReadAsync(), "origin", head), one.Token);

        Assert.Equal((GitWriteOutcome.Rejected, SignInText.NotRenewable), (result.Outcome, result.Message));
    }

    // Updating submodules takes the account of the repository's remote for the submodules on its host: GitHub refusing to
    // renew its token stops the update before Git. A remote Trag cannot trust gives its account to nothing, and the
    // update goes on with Git's own credentials.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenSubmodulesAreUpdatedWithAnAccountGitHubRefusesToRenew_ShouldAskToSignInAgainBeforeGit(bool untrusted)
    {
        using var one = await SubmoduleFixture.CreateAsync();
        using var accounts = new AccountService(Path.Combine(one.Container, "accounts.json"), new FakeSecretStore(), new(new RefusingRenewal(), new FastClock()));
        await accounts.SignInGitHubAsync(false, new Progress<DeviceLoginProgress>(), one.Token);
        await one.GitAsync("remote", "add", "origin", "https://github.com/team/project.git");
        await one.GitAtAsync(Path.Combine(one.Root, "sub"), "commit", "--allow-empty", "-m", "Sub two");
        if (untrusted) await one.GitAsync("config", "remote.origin.mirror", "true");
        var runner = new GitProcessRunner();
        var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader, accounts: accounts, askPassExecutable: Environment.ProcessPath);

        var result = await new GitWriteRouter(runner, sync, reader).ExecuteAsync(new SubmoduleUpdateRequest(await one.ReadAsync()), one.Token);

        Assert.Equal(untrusted ? (GitWriteOutcome.Succeeded, SubmoduleText.Updated) : (GitWriteOutcome.Rejected, SignInText.NotRenewable), (result.Outcome, result.Message));
    }

    // Alice and Bob sign in for the same GitLab, both repositories get a remote there, and while nothing is saved yet
    // One selects Alice and Two selects Bob.
    private static async Task<(AccountInfo Alice, AccountInfo Bob, GitProcessRunner Runner, CliRepositoryReader Reader, GitSyncFacade Sync)>
        SaveAliceForOneAndBobForTwo(GitFixture one, GitFixture two, AccountService accounts)
    {
        var alice = await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, one.Token);
        var bob = await accounts.SignInGitLabAsync("https://gitlab.example:8443/team", "bob-token"u8.ToArray(), false, one.Token);
        foreach (var f in new[] { one, two }) await f.GitAsync("remote", "add", "origin", "https://gitlab.example:8443/team/project.git");
        var runner = new GitProcessRunner(); var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader, accounts: accounts);
        async Task<RepositorySyncState> State(GitFixture f) => await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        async Task Select(GitFixture f, string id)
        {
            var result = await sync.ExecuteAsync(new SyncRequest(await f.ReadAsync(), await State(f), SyncAction.SelectAccount, id), f.Token);
            Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        }
        Assert.Equal(AccountChoice.External, (await State(one)).Accounts!.SelectedId);
        await Select(one, alice.Id); await Select(two, bob.Id);
        return (alice, bob, runner, reader, sync);
    }
    // One's push address moves to another host, a fetch still carrying the earlier state is refused, and One chooses
    // Git's own credentials.
    private static async Task MoveOnesPushAddressAndUseGitsOwnCredentials(GitFixture one, GitSyncFacade sync)
    {
        async Task<RepositorySyncState> State(GitFixture f) => await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        var previous = await State(one);
        await one.GitAsync("remote", "set-url", "--push", "origin", "https://other.example/project.git");
        await sync.ExecuteAsync(new SyncRequest(await one.ReadAsync(), previous, SyncAction.Fetch), one.Token);
        await sync.ExecuteAsync(new SyncRequest(await one.ReadAsync(), await State(one), SyncAction.SelectAccount, AccountChoice.External), one.Token);
    }
    // GitHub's device sign-in hands out a token that expires within a second, and GitHub refuses to renew it.
    private sealed class RefusingRenewal : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var json = request.RequestUri!.AbsolutePath == "/login/device/code"
                ? "{\"device_code\":\"device\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}"
                : request.RequestUri.AbsolutePath == "/user" ? "{\"id\":7,\"login\":\"alice\"}"
                : body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                    ? "{\"error\":\"incorrect_client_credentials\",\"error_description\":\"The client_id and/or client_secret passed are incorrect. token=do-not-display\"}"
                : "{\"access_token\":\"access-one\",\"refresh_token\":\"refresh-one\",\"token_type\":\"bearer\",\"scope\":\"repo\",\"expires_in\":1,\"refresh_token_expires_in\":36000}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    // The device sign-in's waits pass at once.
    private sealed class FastClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { now += dueTime; return new Timer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan); }
    }

    private sealed class IdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(request.Headers.Authorization!.Parameter == "alice-token" ? "{\"id\":1,\"username\":\"alice\"}" : "{\"id\":2,\"username\":\"bob\"}") });
    }
}
