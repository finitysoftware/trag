using System.Net;
using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Platform.Secrets;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts;

public sealed class AccountServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // Two instances of Trag over the same settings and vault, both having read them: an account one removed does not come
    // back from the other without its credential, and one the other added is not dropped while its token stays.
    [Fact]
    public async Task WhenAnotherInstanceRemovedAnAccount_ShouldNotBringItBackWithoutACredential()
    {
        using var f = new Fixture();
        using var first = f.Service(AliceOrBob());
        using var second = f.Service(AliceOrBob());
        var alice = await first.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, Ct);
        Assert.Single((await second.ReadAsync(Ct)).Accounts);

        await first.RemoveAsync(alice.Id, Ct);
        var bob = await second.SignInGitLabAsync("https://gitlab.example:8443/team", "bob-token"u8.ToArray(), false, Ct);

        using var restarted = f.Service(AliceOrBob());
        Assert.Equal([bob.Id], (await restarted.ReadAsync(Ct)).Accounts.Select(account => account.Id));
    }

    [Fact]
    public async Task WhenAnotherInstanceAddedAnAccount_ShouldKeepItWhenThisOneSaves()
    {
        using var f = new Fixture();
        using var first = f.Service(AliceOrBob());
        using var second = f.Service(AliceOrBob());
        Assert.Empty((await second.ReadAsync(Ct)).Accounts);

        var alice = await first.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, Ct);
        var bob = await second.SignInGitLabAsync("https://gitlab.example:8443/team", "bob-token"u8.ToArray(), false, Ct);

        using var restarted = f.Service(AliceOrBob());
        Assert.Equal(new[] { alice.Id, bob.Id }.Order(), (await restarted.ReadAsync(Ct)).Accounts.Select(account => account.Id).Order());
    }

    [Fact]
    public async Task WhenAnotherInstanceSavesAnAccountThisOneKeepsForTheSessionOnly_ShouldListItOnceAsThisSessionsOwn()
    {
        using var f = new Fixture();
        using var first = f.Service(AliceOrBob());
        using var second = f.Service(AliceOrBob());
        await first.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), true, Ct);

        await second.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, Ct);

        Assert.False(Assert.Single((await first.ReadAsync(Ct)).Accounts).IsPersistent);
    }

    [Fact]
    public async Task WhenGitLabIsServedUnderAPortAndAPath_ShouldKeepBothInTheAccountsAddresses()
    {
        using var f = new Fixture();
        using var service = f.Service(AliceOrBob());
        var (alice, _) = await AliceAndBobAsync(service);
        Assert.Equal("https://gitlab.example:8443/team/api/v4/", alice.ApiBaseUri.AbsoluteUri);
        Assert.Equal("https://gitlab.example:8443/team/", alice.TransportOrigin.AbsoluteUri);
    }

    [Fact]
    public async Task WhenTwoGitLabIdentitiesSignIn_ShouldKeepBothTokensOutOfTheSettingsFile()
    {
        using var f = new Fixture();
        using var service = f.Service(AliceOrBob());
        await AliceAndBobAsync(service);
        var json = await File.ReadAllTextAsync(f.Path); Assert.DoesNotContain("alice-token", json); Assert.DoesNotContain("bob-token", json);
    }

    [Fact]
    public async Task WhenTheServiceRestartsAfterTwoGitLabSignIns_ShouldReadBothIdentitiesBack()
    {
        using var f = new Fixture();
        using var service = f.Service(AliceOrBob());
        await AliceAndBobAsync(service);
        using var restarted = f.Service(new Handler((_, _) => Reply("{\"id\":2,\"username\":\"bob\"}")));
        Assert.Equal(2, (await restarted.ReadAsync(Ct)).Accounts.Count);
    }

    [Fact]
    public async Task WhenOneOfTwoGitLabIdentitiesIsRemoved_ShouldDeleteOnlyItsCredential()
    {
        using var f = new Fixture();
        using var service = f.Service(AliceOrBob());
        var (alice, bob) = await AliceAndBobAsync(service);
        using var restarted = await RestartedAsync(f);
        await restarted.RemoveAsync(alice.Id, Ct);
        Assert.Null(await f.Vault.ReadAsync(alice.Id, Ct)); Assert.NotNull(await f.Vault.ReadAsync(bob.Id, Ct));
    }

    [Fact]
    public async Task WhenOneOfTwoGitLabIdentitiesWasRemoved_ShouldStillVerifyTheOther()
    {
        using var f = new Fixture();
        using var service = f.Service(AliceOrBob());
        var (alice, bob) = await AliceAndBobAsync(service);
        using var restarted = await RestartedAsync(f);
        await restarted.RemoveAsync(alice.Id, Ct);
        Assert.Null(await f.Vault.ReadAsync(alice.Id, Ct)); Assert.NotNull(await f.Vault.ReadAsync(bob.Id, Ct));
        Assert.Equal(bob.Id, (await restarted.VerifyAsync(bob.Id, Ct)).Id);
    }

    [Fact]
    public async Task WhenTheVaultIsLocked_ShouldRefuseASavedSignIn()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        await SavedThenLockedAsync(f, service);
        await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct));
    }

    [Fact]
    public async Task WhenTheVaultIsLocked_ShouldRefuseToRemoveTheSavedAccountAndKeepIt()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        var account = await SavedThenLockedAsync(f, service);
        await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct));
        await Assert.ThrowsAsync<AccountException>(() => service.RemoveAsync(account.Id, Ct));
        Assert.Single((await service.ReadAsync(Ct)).Accounts);
    }

    [Fact]
    public async Task WhenTheUserAsksForASessionOnlySignInWhileTheVaultIsLocked_ShouldSignInWithoutSaving()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        await RefusedWhileLockedAsync(f, service);
        var temporary = await service.SignInGitLabAsync("https://gitlab.other", "token"u8.ToArray(), true, Ct);
        Assert.False(temporary.IsPersistent);
    }

    // The connection drops after the provider answered and while its body arrives: the provider could not be reached, as
    // when the connection fails before.
    [Fact]
    public async Task WhenTheConnectionDropsWhileTheAnswerIsRead_ShouldSayTheProviderCannotBeReached()
    {
        using var f = new Fixture();
        using var service = f.Service(new Handler((_, _) => new(HttpStatusCode.OK) { Content = new StreamContent(new DroppedStream()) }));

        var error = await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), true, Ct));

        Assert.Equal("Cannot reach the provider securely. Check your connection, server address and certificate trust.", error.Message);
    }

    private sealed class DroppedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded);
    }

    // A settings file with a member left out or null is a file Trag cannot read, said as such, not a crash.
    [Theory]
    [InlineData("{\"GitHubClientId\":\"id\",\"Accounts\":null}")]
    [InlineData("{\"GitHubClientId\":\"id\",\"Accounts\":[null]}")]
    [InlineData("{\"GitHubClientId\":\"id\",\"Accounts\":[{\"Id\":\"a\",\"Provider\":1,\"ProviderUserId\":\"1\",\"UserName\":\"alice\",\"IsPersistent\":true,\"VerifiedAt\":\"2026-01-01T00:00:00Z\"}]}")]
    public async Task WhenTheSettingsFileHasAMissingMember_ShouldSayItCannotBeRead(string json)
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);
        await File.WriteAllTextAsync(f.Path, json, Ct);
        using var service = f.Service(Alice());

        var error = await Assert.ThrowsAsync<AccountException>(() => service.ReadAsync(Ct));

        Assert.Equal(AccountFailureText.SettingsNotRead, error.Message);
    }

    // A session-only account signed in again to be saved, when the settings file cannot take it: the saved credential goes
    // again, so the credential store keeps nothing the settings do not name.
    [Fact]
    public async Task WhenASessionOnlyAccountIsSavedAndTheSettingsCannotBeWritten_ShouldLeaveNoSavedCredential()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        var temporary = await service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), true, Ct);
        Directory.CreateDirectory(f.Path);

        await Assert.ThrowsAnyAsync<Exception>(() => service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct));

        Assert.DoesNotContain(temporary.Id, f.Vault.Secrets.Keys);
    }

    [Fact]
    public async Task WhenTheServiceRestartsAfterASessionOnlySignIn_ShouldReadOnlyTheSavedAccount()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        await SessionOnlyWhileLockedAsync(f, service);
        using var restarted = f.Service(new Handler((_, _) => throw new InvalidOperationException()));
        Assert.Single((await restarted.ReadAsync(Ct)).Accounts);
    }

    [Fact]
    public async Task WhenTheSessionOnlyAccountIsRemovedWhileTheVaultIsLocked_ShouldLeaveOnlyTheSavedOne()
    {
        using var f = new Fixture();
        using var service = f.Service(Alice());
        var temporary = await SessionOnlyWhileLockedAsync(f, service);
        using var restarted = f.Service(new Handler((_, _) => throw new InvalidOperationException()));
        Assert.Single((await restarted.ReadAsync(Ct)).Accounts);
        await service.RemoveAsync(temporary.Id, Ct);
        Assert.Single((await service.ReadAsync(Ct)).Accounts);
    }

    [Fact]
    public async Task WhenGitHubIsStillWaitingForTheUser_ShouldPollAgainAfterTheInterval()
    {
        using var f = new Fixture(); var github = new SlowGitHub(); var pollTimes = github.PollTimes;
        using var service = f.Service(github.Handler(), github.Clock);
        await service.SignInGitHubAsync(false, new ProgressSink(github.Progress), Ct);
        Assert.Equal(5, (pollTimes[1] - pollTimes[0]).TotalSeconds);
    }

    [Fact]
    public async Task WhenGitHubAsksToSlowDown_ShouldPollAtTheLongerIntervalAndSaySo()
    {
        using var f = new Fixture(); var github = new SlowGitHub(); var (pollTimes, progress) = (github.PollTimes, github.Progress);
        using var service = f.Service(github.Handler(), github.Clock);
        await service.SignInGitHubAsync(false, new ProgressSink(progress), Ct);
        Assert.Equal(10, (pollTimes[2] - pollTimes[1]).TotalSeconds);
        Assert.Contains(progress, p => p.State == DeviceLoginState.SlowedDown);
    }

    [Fact]
    public async Task WhenAGitHubTokenAboutToExpireIsVerified_ShouldRenewItOnce()
    {
        using var f = new Fixture(); var github = new SlowGitHub();
        using var service = f.Service(github.Handler(), github.Clock);
        var account = await service.SignInGitHubAsync(false, new ProgressSink(github.Progress), Ct);
        var verified = await service.VerifyAsync(account.Id, Ct);
        var refreshes = github.Refreshes;
        Assert.Equal(account.Id, verified.Id); Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task WhenAGitHubTokenWasRenewed_ShouldStoreOnlyTheNewRefreshToken()
    {
        using var f = new Fixture(); var github = new SlowGitHub();
        using var service = f.Service(github.Handler(), github.Clock);
        var account = await RenewedAsync(service, github);
        var stored = Encoding.UTF8.GetString((await f.Vault.ReadAsync(account.Id, Ct))!);
        Assert.Contains("refresh-two", stored); Assert.DoesNotContain("refresh-one", stored);
    }

    [Fact]
    public async Task WhenAGitHubTokenWasRenewed_ShouldKeepEveryAccessTokenOutOfTheSettingsFile()
    {
        using var f = new Fixture(); var github = new SlowGitHub();
        using var service = f.Service(github.Handler(), github.Clock);
        await RenewedAsync(service, github);
        Assert.DoesNotContain("access-", await File.ReadAllTextAsync(f.Path));
    }

    [Theory]
    [InlineData("access_denied", "denied")]
    [InlineData("expired_token", "expired")]
    [InlineData("device_flow_disabled", "does not accept Trag's sign-in")]
    [InlineData("incorrect_client_credentials", "does not accept Trag's sign-in")]
    public async Task WhenGitHubEndsTheDeviceSignInWithAnError_ShouldExplainItWithoutItsDescriptionAndSaveNoAccount(string error, string expected)
    {
        using var f = new Fixture();
        using var service = f.Service(new Handler((r, _) => Reply(r.RequestUri!.AbsolutePath == "/login/device/code" ? Code() : "{\"error\":\"" + error + "\",\"error_description\":\"do-not-display-token\"}")), new FastClock());
        var failure = await Assert.ThrowsAsync<AccountException>(() => service.SignInGitHubAsync(false, new ProgressSink([]), Ct));
        Assert.Contains(expected, failure.Message); Assert.DoesNotContain("do-not-display", failure.Message);
        Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    // Trag signs in with its own registered application; a Client ID an older settings file still names is not used.
    [Fact]
    public async Task WhenTheSettingsFileNamesAnotherClientId_ShouldSignInWithTragsOwn()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);
        await File.WriteAllTextAsync(f.Path, "{\"GitHubClientId\":\"Ov23liOTHERclient\",\"Accounts\":[]}");
        var asked = new List<string>();
        using var service = f.Service(new Handler((r, body) =>
        {
            if (r.RequestUri!.AbsolutePath == "/login/device/code") { asked.Add(body); return Reply(Code()); }
            if (r.RequestUri.AbsolutePath == "/user") return Reply("{\"id\":7,\"login\":\"alice\"}");
            return Reply(Token("access-one", "refresh-one", 3600));
        }), new FastClock());

        await service.SignInGitHubAsync(false, new ProgressSink([]), Ct);

        Assert.Contains("client_id=Ov23liBO0BGpinG9cmk4&", Assert.Single(asked));
    }

    [Fact]
    public async Task WhenTheRenewedTokenBelongsToSomebodyElse_ShouldRefuseABroker()
    {
        using var f = new Fixture();
        using var service = f.Service(RenewingToSomebodyElse(), new FastClock());
        var account = await service.SignInGitHubAsync(false, new ProgressSink([]), Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
    }

    // A renewal GitHub refuses leaves one thing to do, whatever its code says: sign in again. GitHub's answer goes to the
    // details, without anything that looks like a secret.
    [Fact]
    public async Task WhenGitHubRefusesToRenewAToken_ShouldAskToSignInAgainAndKeepItsAnswerForTheDetails()
    {
        using var f = new Fixture();
        using var service = f.Service(RefusingTheRenewal(), new FastClock());
        var account = await service.SignInGitHubAsync(false, new ProgressSink([]), Ct);
        var failure = await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
        Assert.Equal(SignInText.NotRenewable, failure.Message);
        Assert.Contains("incorrect_client_credentials", failure.Details);
        Assert.Contains("The client_id and/or client_secret passed are incorrect.", failure.Details);
        Assert.DoesNotContain("do-not-display", failure.Details);
    }

    // GitHub takes each refresh token once, so a renewed token the credential store refuses is lost with the old one. The
    // account says so at once and again on the next try: it needs a new sign-in.
    [Fact]
    public async Task WhenTheVaultWriteFailsAfterGitHubRotatedTheToken_ShouldKeepAUsableAccountOrSaySignInAgain()
    {
        using var f = new Fixture(); var renewals = 0;
        using var service = f.Service(new Handler((r, body) =>
        {
            if (r.RequestUri!.AbsolutePath == "/login/device/code") return Reply(Code());
            if (r.RequestUri.AbsolutePath == "/user") return Reply("{\"id\":7,\"login\":\"alice\"}");
            if (!body.Contains("grant_type=refresh_token", StringComparison.Ordinal)) return Reply(Token("access-one", "refresh-one", 1));
            if (++renewals > 1) return Reply("{\"error\":\"bad_refresh_token\",\"error_description\":\"The refresh token passed is incorrect or expired.\"}");
            f.Vault.Failure = new SecretStoreException(SecretStoreStatus.Unavailable);
            return Reply(Token("access-two", "refresh-two", 3600));
        }), new FastClock());
        var account = await service.SignInGitHubAsync(false, new ProgressSink([]), Ct);

        var lost = await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
        f.Vault.Failure = null;
        var refused = await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));

        Assert.Equal(AccountFailureText.CredentialNotSaved, lost.Message);
        Assert.Equal(SignInText.NotRenewable, refused.Message);
    }

    // Two instances of Trag renew the same GitHub token at once, as an auto-fetch in each would after eight hours. GitHub
    // accepts a refresh token once, so the second waits for the first and uses the token it stored, instead of saying to
    // sign in again. The first renewal holds GitHub's answer until the other instance's arrives, or three seconds pass.
    [Fact]
    public async Task WhenTwoInstancesRenewTheSameTokenAtOnce_ShouldRenewItOnceAndBothUseTheRenewedToken()
    {
        using var f = new Fixture(); using var other = new ManualResetEventSlim(); var renewals = 0; var used = 0;
        var gitHub = new Handler((r, body) =>
        {
            if (r.RequestUri!.AbsolutePath == "/login/device/code") return Reply(Code());
            if (r.RequestUri.AbsolutePath == "/user") return Reply("{\"id\":7,\"login\":\"alice\"}");
            if (!body.Contains("grant_type=refresh_token", StringComparison.Ordinal)) return Reply(Token("access-one", "refresh-one", 1));
            if (Interlocked.Increment(ref renewals) == 1) other.Wait(TimeSpan.FromSeconds(3)); else other.Set();
            return body.Contains("refresh_token=refresh-one", StringComparison.Ordinal) && Interlocked.Exchange(ref used, 1) == 0
                ? Reply(Token("access-two", "refresh-two", 3600))
                : Reply("{\"error\":\"bad_refresh_token\",\"error_description\":\"The refresh token passed is incorrect or expired.\"}");
        });
        using var one = f.Service(gitHub, new FastClock());
        var account = await one.SignInGitHubAsync(false, new ProgressSink([]), Ct);
        using var two = f.Service(gitHub, new FastClock());

        var brokers = await Task.WhenAll(one.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct),
            two.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));

        var passwords = brokers.Select(broker => broker.Authorize(new(broker.OperationId, "Password for 'https://alice@github.com/team/project.git': "))).ToArray();
        foreach (var broker in brokers) await broker.DisposeAsync();
        Assert.Equal(1, renewals);
        Assert.All(passwords, password => Assert.Equal("access-two"u8.ToArray(), password));
    }

    // A session without a settings file shares its credentials with no other instance, so its renewal takes no lock,
    // and none lands in the folder the process happens to run in.
    [Fact]
    public async Task WhenASessionWithoutASettingsFileRenewsAToken_ShouldTakeNoLockFile()
    {
        var stray = System.IO.Path.Combine(Environment.CurrentDirectory, ".renewal.lock");
        var github = new SlowGitHub();
        using var service = new AccountService(null, new FakeSecretStore(), new(github.Handler(), github.Clock));
        var account = await service.SignInGitHubAsync(true, new ProgressSink(github.Progress), Ct);

        await using var broker = await service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct);

        Assert.Equal(1, github.Refreshes);
        Assert.False(File.Exists(stray));
    }

    // The credential goes first, so a settings file that cannot be saved leaves the account listed without one, never a
    // token in the credential store that no account names; removing it again finishes the removal. Windows refuses to
    // replace a read-only file.
    [Fact]
    public async Task WhenRemoveDeletesTheCredentialAndTheSettingsSaveFails_ShouldKeepTheAccountListedUntilRemovedAgain()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); using var service = f.Service(Alice());
        var account = await service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct);
        File.SetAttributes(f.Path, FileAttributes.ReadOnly);

        var failure = await Assert.ThrowsAsync<AccountException>(() => service.RemoveAsync(account.Id, Ct));
        File.SetAttributes(f.Path, FileAttributes.Normal);

        Assert.Equal(AccountFailureText.SettingsNotSaved, failure.Message);
        Assert.DoesNotContain(account.Id, f.Vault.Secrets.Keys);
        Assert.Single((await service.ReadAsync(Ct)).Accounts);
        await service.RemoveAsync(account.Id, Ct);
        Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    // A connection that drops while GitHub is still waiting for the user ends the sign-in with the sentence for an
    // unreachable provider, and nothing is saved.
    [Fact]
    public async Task WhenTheNetworkDropsWhileGitHubIsWaitingForTheUser_ShouldSayTheProviderCannotBeReachedAndSaveNoAccount()
    {
        using var f = new Fixture();
        using var service = f.Service(new Handler((r, _) => r.RequestUri!.AbsolutePath == "/login/device/code" ? Reply(Code()) : throw new HttpRequestException("connection reset")), new FastClock());

        var error = await Assert.ThrowsAsync<AccountException>(() => service.SignInGitHubAsync(false, new ProgressSink([]), Ct));

        Assert.Equal(ProviderText.Unreachable, error.Message);
        Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    // The failed identity check stays required across a restart.
    [Fact]
    public async Task WhenTheProviderStillNamesSomebodyElseAfterARestart_ShouldStillRefuseABroker()
    {
        using var f = new Fixture();
        using var service = f.Service(RenewingToSomebodyElse(), new FastClock());
        var account = await RefusedAfterTheRenewalAsync(service);
        using var restarted = f.Service(new Handler((_, _) => Reply("{\"id\":999,\"login\":\"wrong-account\"}")));
        await Assert.ThrowsAsync<AccountException>(() => restarted.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
    }

    [Fact]
    public async Task WhenTheProviderConfirmsTheIdentityAfterARestart_ShouldReleaseTheRenewedToken()
    {
        using var f = new Fixture();
        using var service = f.Service(RenewingToSomebodyElse(), new FastClock());
        var account = await RefusedAfterTheRenewalAsync(service);
        using var restarted = f.Service(new Handler((_, _) => Reply("{\"id\":999,\"login\":\"wrong-account\"}")));
        await Assert.ThrowsAsync<AccountException>(() => restarted.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
        using var recovered = f.Service(new Handler((_, _) => Reply("{\"id\":7,\"login\":\"alice\"}")));
        await using var broker = await recovered.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct);
        Assert.Equal("access-two"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@github.com/team/project.git': ")));
    }

    [Fact]
    public async Task WhenTheDeviceCodeExpiresWhileGitHubIsWaiting_ShouldStopPollingAndSaySo()
    {
        using var f = new Fixture(); var calls = 0;
        using var service = f.Service(WaitingUntilTheCodeExpires(() => calls++), new FastClock());
        Assert.Contains("expired", (await Assert.ThrowsAsync<AccountException>(() => service.SignInGitHubAsync(false, new ProgressSink([]), Ct))).Message);
        Assert.Equal(2, calls);
    }

    // A sign-in whose code expired came first, on the same settings and vault.
    [Fact]
    public async Task WhenTheTokenLacksTheRepoScope_ShouldNameItAndSaveNoAccount()
    {
        using var f = new Fixture();
        using var service = f.Service(WaitingUntilTheCodeExpires(() => { }), new FastClock());
        await Assert.ThrowsAsync<AccountException>(() => service.SignInGitHubAsync(false, new ProgressSink([]), Ct));
        using var denied = f.Service(new Handler((r, _) => Reply(r.RequestUri!.AbsolutePath == "/login/device/code" ? Code() : "{\"access_token\":\"hidden-token\",\"token_type\":\"bearer\",\"scope\":\"read:user\"}")), new FastClock());
        Assert.Contains("repo", (await Assert.ThrowsAsync<AccountException>(() => denied.SignInGitHubAsync(false, new ProgressSink([]), Ct))).Message);
        Assert.Empty((await denied.ReadAsync(Ct)).Accounts);
    }

    [Fact]
    public async Task WhenTheGitHubSignInIsCancelled_ShouldSaveNoAccount()
    {
        using var f = new Fixture(); using var cancel = new CancellationTokenSource();
        using var service = f.Service(new Handler((_, _) => { cancel.Cancel(); return Reply(Code()); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SignInGitHubAsync(false, new ProgressSink([]), cancel.Token));
        Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    [Theory]
    [InlineData(401, "expired")]
    [InlineData(403, "permissions")]
    [InlineData(302, "HTTPS address")]
    public async Task WhenGitLabAnswersTheSignInWithAnError_ShouldExplainItWithoutTheBodyAndSaveNoAccount(int status, string expected)
    {
        using var f = new Fixture();
        using var service = f.Service(new Handler((_, _) => new((HttpStatusCode)status) { Content = new StringContent("very-secret-token") }));
        var error = await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.com", "very-secret-token"u8.ToArray(), false, Ct));
        Assert.Contains(expected, error.Message); Assert.DoesNotContain("very-secret", error.Message); Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    [Theory]
    [InlineData("http://gitlab.example")]
    [InlineData("https://user:password@gitlab.example")]
    [InlineData("https://gitlab.example?token=secret")]
    public async Task WhenTheGitLabAddressIsUnsafe_ShouldRefuseItBeforeSendingTheToken(string address)
    {
        using var f = new Fixture(); using var service = f.Service(new Handler((_, _) => throw new InvalidOperationException("Should not send")));
        await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync(address, "token"u8.ToArray(), true, Ct));
    }

    [Fact]
    public async Task WhenABrokerIsAskedForAnotherPort_ShouldRefuseIt()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        var a = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "only-for-this-repo"u8.ToArray(), false, Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(a.Id, new("https://gitlab.example/team/project.git"), Ct));
    }

    [Fact]
    public async Task WhenABrokerIsAskedForOutsideTheAccountsBasePath_ShouldRefuseIt()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        var a = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "only-for-this-repo"u8.ToArray(), false, Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(a.Id, new("https://gitlab.example/team/project.git"), Ct));
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(a.Id, new("https://gitlab.example:8443/other/project.git"), Ct));
    }

    // Submodules ask for the whole host, which for an instance served under a path is the whole instance: every repository
    // under its path gets the token, and nothing else on the host does.
    [Fact]
    public async Task WhenTheWholeHostIsAskedForAnInstanceUnderAPath_ShouldAnswerForEveryRepositoryOfTheInstanceOnly()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        var a = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "only-for-the-instance"u8.ToArray(), false, Ct);

        await using var broker = await service.CreateBrokerAsync(a.Id, new("https://gitlab.example:8443/"), Ct);

        Assert.Equal("only-for-the-instance"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/group/sub.git': ")));
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/other/sub.git': ")));
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/teamwork/sub.git': ")));
    }

    // Git asks for a repository by its address without the slashes at its end, and Git 2.55 writes a space in its path as
    // %20 where Git 2.43 writes the space itself (experiment with a server that answers 401, 31.10.2026).
    [Theory]
    [InlineData("https://gitlab.example/team/project.git/", "team/project.git")]
    [InlineData("https://gitlab.example/team/project.git//", "team/project.git")]
    [InlineData("https://gitlab.example/my%20team/project.git", "my%20team/project.git")]
    [InlineData("https://gitlab.example/my%20team/project.git", "my team/project.git")]
    public async Task WhenTheRemoteUrlHasATrailingSlashOrPercentEncodedPath_ShouldStillMatchTheEndpoint(string remote, string asked)
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        var a = await service.SignInGitLabAsync("https://gitlab.example", "for-this-repo"u8.ToArray(), false, Ct);

        await using var broker = await service.CreateBrokerAsync(a.Id, new(remote), Ct);

        Assert.Equal("for-this-repo"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example/" + asked + "': ")));
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example/" + asked + "/other.git': ")));
    }

    [Fact]
    public async Task WhenGitAsksTheBrokerAboutAnotherRepository_ShouldAnswerNothing()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        await using var broker = await ProjectBrokerAsync(service);
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/other.git': ")));
    }

    [Fact]
    public async Task WhenGitAsksTheBrokerAboutItsRepository_ShouldAnswerWithTheToken()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        await using var broker = await ProjectBrokerAsync(service);
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/other.git': ")));
        Assert.Equal("only-for-this-repo"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/project.git': ")));
    }

    [Fact]
    public async Task WhenTheServerEchoesTheToken_ShouldRedactIt()
    {
        using var f = new Fixture(); using var service = f.Service(Alice());
        await using var broker = await ProjectBrokerAsync(service);
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/other.git': ")));
        Assert.Equal("only-for-this-repo"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@gitlab.example:8443/team/project.git': ")));
        Assert.DoesNotContain("only-for-this-repo", broker.Redact("Server echoes only-for-this-repo"));
    }

    // GitLab answers as alice for alice's token and as bob for any other.
    private static Handler AliceOrBob() =>
        new((r, _) => Reply(r.Headers.Authorization!.Parameter == "alice-token" ? "{\"id\":1,\"username\":\"alice\"}" : "{\"id\":2,\"username\":\"bob\"}"));

    private static Handler Alice() => new((_, _) => Reply("{\"id\":1,\"username\":\"alice\"}"));

    // Alice, then bob, signed in and saved on the same GitLab under a port and a path.
    private static async Task<(AccountInfo Alice, AccountInfo Bob)> AliceAndBobAsync(AccountService service)
    {
        var alice = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "alice-token"u8.ToArray(), false, Ct);
        var bob = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "bob-token"u8.ToArray(), false, Ct);
        return (alice, bob);
    }

    // A second service over the same settings and vault, after it read them; its GitLab answers as bob.
    private static async Task<AccountService> RestartedAsync(Fixture f)
    {
        var restarted = f.Service(new Handler((_, _) => Reply("{\"id\":2,\"username\":\"bob\"}")));
        await restarted.ReadAsync(Ct);
        return restarted;
    }

    // A saved account, then the vault locks.
    private static async Task<AccountInfo> SavedThenLockedAsync(Fixture f, AccountService service)
    {
        var account = await service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct);
        f.Vault.Status = SecretStoreStatus.Locked;
        return account;
    }

    // With the vault locked, a saved sign-in and the removal of the saved account were refused.
    private static async Task RefusedWhileLockedAsync(Fixture f, AccountService service)
    {
        var account = await SavedThenLockedAsync(f, service);
        await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), false, Ct));
        await Assert.ThrowsAsync<AccountException>(() => service.RemoveAsync(account.Id, Ct));
        Assert.Single((await service.ReadAsync(Ct)).Accounts);
    }

    // After the refusals, a session-only account signed in on another GitLab.
    private static async Task<AccountInfo> SessionOnlyWhileLockedAsync(Fixture f, AccountService service)
    {
        await RefusedWhileLockedAsync(f, service);
        return await service.SignInGitLabAsync("https://gitlab.other", "token"u8.ToArray(), true, Ct);
    }

    // Signed in through the slow GitHub, then verified, which renewed the token that was about to expire.
    private static async Task<AccountInfo> RenewedAsync(AccountService service, SlowGitHub github)
    {
        var account = await service.SignInGitHubAsync(false, new ProgressSink(github.Progress), Ct);
        await service.VerifyAsync(account.Id, Ct);
        return account;
    }

    // The first token expires within a second; once it is renewed, GitHub names another identity.
    private static Handler RenewingToSomebodyElse()
    {
        var renewed = false;
        return new((r, body) =>
        {
            if (r.RequestUri!.AbsolutePath == "/login/device/code") return Reply(Code());
            if (r.RequestUri.AbsolutePath == "/user") return Reply(renewed ? "{\"id\":999,\"login\":\"wrong-account\"}" : "{\"id\":7,\"login\":\"alice\"}");
            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal)) { renewed = true; return Reply(Token("access-two", "refresh-two", 3600)); }
            return Reply(Token("access-one", "refresh-one", 1));
        });
    }

    // The first token expires within a second, and GitHub refuses to renew it.
    private static Handler RefusingTheRenewal() => new((r, body) =>
    {
        if (r.RequestUri!.AbsolutePath == "/login/device/code") return Reply(Code());
        if (r.RequestUri.AbsolutePath == "/user") return Reply("{\"id\":7,\"login\":\"alice\"}");
        if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            return Reply("{\"error\":\"incorrect_client_credentials\",\"error_description\":\"The client_id and/or client_secret passed are incorrect. token=do-not-display\"}");
        return Reply(Token("access-one", "refresh-one", 1));
    });

    // Signed in, then a broker was refused because the renewed token named somebody else.
    private static async Task<AccountInfo> RefusedAfterTheRenewalAsync(AccountService service)
    {
        var account = await service.SignInGitHubAsync(false, new ProgressSink([]), Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(account.Id, new("https://github.com/team/project.git"), Ct));
        return account;
    }

    // A ten-second code that GitHub never sees approved.
    private static Handler WaitingUntilTheCodeExpires(Action called) =>
        new((r, _) => { called(); return Reply(r.RequestUri!.AbsolutePath == "/login/device/code" ? Code(10) : "{\"error\":\"authorization_pending\"}"); });

    // Signed in on GitLab under port 8443 and /team; brokers for another port and another path were refused first.
    private static async Task<CredentialBroker> ProjectBrokerAsync(AccountService service)
    {
        var a = await service.SignInGitLabAsync("https://gitlab.example:8443/team", "only-for-this-repo"u8.ToArray(), false, Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(a.Id, new("https://gitlab.example/team/project.git"), Ct));
        await Assert.ThrowsAsync<AccountException>(() => service.CreateBrokerAsync(a.Id, new("https://gitlab.example:8443/other/project.git"), Ct));
        return await service.CreateBrokerAsync(a.Id, new("https://gitlab.example:8443/team/project.git"), Ct);
    }

    internal static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string Code(int expires = 900) => "{\"device_code\":\"private-device-code\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":" + expires + ",\"interval\":5}";
    private static string Token(string access, string refresh, int expires) => "{\"access_token\":\"" + access + "\",\"refresh_token\":\"" + refresh + "\",\"token_type\":\"bearer\",\"scope\":\"repo,read:user\",\"expires_in\":" + expires + ",\"refresh_token_expires_in\":36000}";
    private sealed class ProgressSink(List<DeviceLoginProgress> values) : IProgress<DeviceLoginProgress> { public void Report(DeviceLoginProgress value) => values.Add(value); }
    internal sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
    }
    // GitHub keeps the user waiting once, asks to slow down to ten seconds once, then hands out a token that expires
    // within a second and renews it when asked. No request may carry a client secret: the device flow has none.
    private sealed class SlowGitHub
    {
        private int polls;
        public FastClock Clock { get; } = new();
        public List<DateTimeOffset> PollTimes { get; } = [];
        public List<DeviceLoginProgress> Progress { get; } = [];
        public int Refreshes { get; private set; }
        public Handler Handler() => new((r, body) =>
        {
            Assert.DoesNotContain("client_secret", body);
            if (r.RequestUri!.AbsolutePath == "/login/device/code") return Reply(Code());
            if (r.RequestUri.AbsolutePath == "/user") return Reply("{\"id\":7,\"login\":\"alice\"}");
            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            { Refreshes++; Assert.Contains("refresh_token=refresh-one", body); return Reply(Token("access-two", "refresh-two", 3600)); }
            PollTimes.Add(Clock.GetUtcNow());
            return Reply(++polls switch { 1 => "{\"error\":\"authorization_pending\"}", 2 => "{\"error\":\"slow_down\",\"interval\":10}", _ => Token("access-one", "refresh-one", 1) });
        });
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trag-accounts-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(root, "accounts.json");
        public FakeSecretStore Vault { get; } = new();
        public AccountService Service(HttpMessageHandler handler, TimeProvider? clock = null) => new(Path, Vault, new(handler, clock));
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class FastClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { now += dueTime; return new Timer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan); }
    }
}
