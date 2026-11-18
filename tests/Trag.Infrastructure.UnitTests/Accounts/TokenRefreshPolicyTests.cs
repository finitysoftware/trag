using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Providers;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts;

// What has to happen before a credential may be used. A token near its expiry is renewed, the renewed one is stored
// before any further network work, and it is only trusted once the provider confirms the same person.
public sealed class TokenRefreshPolicyTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri GitHubApi = new("https://api.github.com/");

    private static AccountInfo GitHub() =>
        new(AccountIdentity.StableId(AccountProvider.GitHub, GitHubApi, "7"), AccountProvider.GitHub, "7", "alice",
            GitHubApi, new("https://github.com/"), true, DateTimeOffset.UnixEpoch, null);

    private static TokenData Expiring() => new()
    {
        AccessToken = "old-access", RefreshToken = "old-refresh", ClientId = "client",
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30), RefreshExpiresAt = DateTimeOffset.UtcNow.AddHours(10)
    };

    private const string Renewed = "{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"token_type\":\"bearer\",\"scope\":\"repo\",\"expires_in\":3600}";

    [Fact]
    public async Task WhenATokenIsStillFarFromItsExpiry_ShouldUseItAsItIs()
    {
        using var f = new Fixture(_ => throw new InvalidOperationException("Should not reach the provider"));
        var token = await f.ApplyAsync(GitHub(), new() { AccessToken = "current", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        Assert.Equal("current", token.AccessToken);
        Assert.Empty(f.Writes);
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task WhenAGitHubTokenIsAboutToExpire_ShouldStoreTheRenewedOneAndRecordThatTheIdentityWasChecked()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/user"
            ? AccountServiceTests.Reply("{\"id\":7,\"login\":\"alice-renamed\"}") : AccountServiceTests.Reply(Renewed));
        var account = GitHub();
        var token = await f.ApplyAsync(account, Expiring());

        Assert.Equal("new-access", token.AccessToken);
        Assert.False(token.RequiresIdentityVerification);
        // Stored twice: the moment the secret rotated, and again once the identity was confirmed.
        Assert.Equal(2, f.Writes.Count);
        Assert.Contains("new-refresh", f.Stored(account));
        var updated = Assert.Single(f.Updates);
        Assert.Equal("alice-renamed", updated.UserName);
        Assert.Equal(token.ExpiresAt, updated.ExpiresAt);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, updated.VerifiedAt);
    }

    [Fact]
    public async Task WhenTheRenewedTokenBelongsToSomebodyElse_ShouldRefuseItAfterStoringTheRotation()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/user"
            ? AccountServiceTests.Reply("{\"id\":999,\"login\":\"somebody-else\"}") : AccountServiceTests.Reply(Renewed));
        var account = GitHub();
        Assert.Equal("The renewed token belongs to another identity. Sign in again.",
            (await Assert.ThrowsAsync<AccountException>(() => f.ApplyAsync(account, Expiring()))).Message);
        // The revoked refresh token is gone from the store and the rotated one is still marked for verification,
        // so a restart asks the provider again instead of using it.
        var stored = f.Stored(account);
        Assert.Contains("new-refresh", stored);
        Assert.DoesNotContain("old-refresh", stored);
        Assert.Contains("\"RequiresIdentityVerification\":true", stored);
        Assert.Single(f.Writes);
        Assert.Empty(f.Updates);
    }

    [Fact]
    public async Task WhenATokenIsMarkedForVerification_ShouldCheckItWithoutRenewingIt()
    {
        using var f = new Fixture(request => request.RequestUri!.AbsolutePath == "/user"
            ? AccountServiceTests.Reply("{\"id\":7,\"login\":\"alice\"}") : throw new InvalidOperationException("Should not refresh"));
        var token = await f.ApplyAsync(GitHub(), new() { AccessToken = "current", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RequiresIdentityVerification = true });
        Assert.False(token.RequiresIdentityVerification);
        Assert.Single(f.Writes);
        Assert.Single(f.Updates);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FakeSecretStore store = new();
        private readonly TokenVault vault;
        private readonly ProviderClient provider;
        private readonly TokenRefreshPolicy policy;

        public List<string> Writes => store.Writes;
        public List<AccountInfo> Updates { get; } = [];

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            provider = new(new AccountServiceTests.Handler((request, _) => handler(request)));
            vault = new(store);
            policy = new(provider, vault, _ => Task.FromResult<IAsyncDisposable?>(null), (account, _) => { Updates.Add(account); return Task.CompletedTask; });
        }

        public async Task<TokenData> ApplyAsync(AccountInfo account, TokenData token)
        {
            await vault.WriteAsync(account, token, Ct);
            store.Writes.Clear();
            return await policy.ApplyAsync(account, token, Ct);
        }

        public string Stored(AccountInfo account) => Encoding.UTF8.GetString(store.ReadAsync(account.Id, Ct).GetAwaiter().GetResult()!);

        public void Dispose() { vault.Dispose(); provider.Dispose(); }
    }
}
