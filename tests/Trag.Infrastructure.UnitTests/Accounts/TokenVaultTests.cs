using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts;

// What a stored credential can be wrong in, each with its own sentence: the store cannot be reached, the credential is
// not there, or what is there is no longer a token. None of them is corrected by typing a token in.
public sealed class TokenVaultTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly AccountInfo Saved = FakeAccountService.GitHub();

    [Fact]
    public async Task WhenTheCredentialIsMissing_ShouldAskToSignInAgain()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        Assert.Equal("The saved credential is missing. Sign in to this account again.",
            (await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct))).Message);
    }

    [Fact]
    public async Task WhenTheCredentialIsNotJson_ShouldSayItCannotBeRead()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        await MissingAsync(vault);

        store.Secrets[Saved.Id] = Encoding.UTF8.GetBytes("not json");
        Assert.Equal("This credential cannot be read. Sign in again.",
            (await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct))).Message);
    }

    [Fact]
    public async Task WhenTheStoredAccessTokenIsNotAToken_ShouldSayItCannotBeRead()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        await MissingThenNotJsonAsync(store, vault);

        store.Secrets[Saved.Id] = Encoding.UTF8.GetBytes("{\"AccessToken\":\"has space\"}");
        Assert.Equal("This credential cannot be read. Sign in again.",
            (await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct))).Message);
    }

    // The damaged credential is still in the store; a locked store hides it.
    [Fact]
    public async Task WhenTheStoreIsLocked_ShouldAskToUnlockIt()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        await MissingThenNotJsonAsync(store, vault);
        store.Secrets[Saved.Id] = Encoding.UTF8.GetBytes("{\"AccessToken\":\"has space\"}");
        await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct));

        store.Status = SecretStoreStatus.Locked;
        Assert.Equal("Your OS credential store is locked or unavailable. Unlock it and retry.",
            (await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct))).Message);
    }

    [Fact]
    public async Task WhenASessionOnlyAccountsCredentialIsWritten_ShouldKeepItInMemoryOutOfTheStore()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        var session = await SessionThenSavedWrittenAsync(vault);

        Assert.Equal("session-token", (await vault.ReadAsync(session, Ct)).AccessToken);
        Assert.Equal([Saved.Id], store.Writes);
    }

    [Fact]
    public async Task WhenASavedAccountsCredentialIsWritten_ShouldKeepItInTheStore()
    {
        var store = new FakeSecretStore();
        using var vault = new TokenVault(store);
        var session = await SessionThenSavedWrittenAsync(vault);
        Assert.Equal("session-token", (await vault.ReadAsync(session, Ct)).AccessToken);

        Assert.Equal("saved-token", (await vault.ReadAsync(Saved, Ct)).AccessToken);
        Assert.Equal([Saved.Id], store.Writes);
    }

    // The first read found no credential at all.
    private static async Task MissingAsync(TokenVault vault) =>
        await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct));

    // ... and the next one found a credential that is not JSON.
    private static async Task MissingThenNotJsonAsync(FakeSecretStore store, TokenVault vault)
    {
        await MissingAsync(vault);
        store.Secrets[Saved.Id] = Encoding.UTF8.GetBytes("not json");
        await Assert.ThrowsAsync<AccountException>(() => vault.ReadAsync(Saved, Ct));
    }

    // A session-only account's credential, then the saved account's.
    private static async Task<AccountInfo> SessionThenSavedWrittenAsync(TokenVault vault)
    {
        var session = Saved with { Id = "session-account", IsPersistent = false };
        await vault.WriteAsync(session, new() { AccessToken = "session-token" }, Ct);
        await vault.WriteAsync(Saved, new() { AccessToken = "saved-token" }, Ct);
        return session;
    }
}
