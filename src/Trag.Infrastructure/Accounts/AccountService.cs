using System.Security.Cryptography;
using System.Text;
using Trag.Application.Ports.Accounts;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Accounts.Providers;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Infrastructure.Accounts;

// Every account operation in one place, each one running alone: the gate is taken first, the settings file is read
// once, and the credential is read, renewed and verified before anything is done with it.
public sealed class AccountService : IAccountService, IGitAccounts, IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly AccountRegistry registry = new();
    private readonly AccountSettingsFile file;
    private readonly TokenVault vault;
    private readonly TokenRefreshPolicy refresh;
    private readonly ProviderClient provider;

    public AccountService(string? settingsPath, ISecretStore vault, ProviderClient provider)
    {
        this.provider = provider;
        file = new(settingsPath);
        this.vault = new(vault);
        refresh = new(provider, this.vault, file.RenewalLockAsync, UpdateAsync);
    }

    public Task<AccountSettings> ReadAsync(CancellationToken ct) => LockedAsync(ct, async () =>
        new AccountSettings(registry.All, await vault.StatusAsync(ct).ConfigureAwait(false)));

    public async Task<AccountInfo> SignInGitHubAsync(bool memoryOnly, IProgress<DeviceLoginProgress> progress, CancellationToken ct)
    {
        var settings = await ReadAsync(ct).ConfigureAwait(false);
        RequireStorage(settings.VaultStatus, memoryOnly);
        var token = await provider.DeviceLoginAsync(progress, ct).ConfigureAwait(false);
        return await AddAsync(AccountProvider.GitHub, AccountMapping.GitHubApi, AccountMapping.GitHubOrigin, token, memoryOnly, ct).ConfigureAwait(false);
    }
    public async Task<AccountInfo> SignInGitLabAsync(string instanceUrl, ReadOnlyMemory<byte> token, bool memoryOnly, CancellationToken ct)
    {
        Uri api;
        try { api = AccountMapping.GitLabApiBase(instanceUrl); }
        catch (ArgumentException) { throw new AccountException(AccountFailureText.GitLabAddressInvalid); }
        RequireStorage((await ReadAsync(ct).ConfigureAwait(false)).VaultStatus, memoryOnly);
        var value = Encoding.UTF8.GetString(token.Span);
        TokenValidation.Validate(value);
        return await AddAsync(AccountProvider.GitLab, api, AccountMapping.GitLabOrigin(api), new() { AccessToken = value }, memoryOnly, ct).ConfigureAwait(false);
    }
    private async Task<AccountInfo> AddAsync(AccountProvider kind, Uri api, Uri origin, TokenData token, bool memoryOnly, CancellationToken ct)
    {
        var identity = await provider.IdentityAsync(kind, api, token.AccessToken, ct).ConfigureAwait(false);
        var id = AccountIdentity.StableId(kind, api, identity.Id);
        var account = new AccountInfo(id, kind, identity.Id, identity.UserName, api, origin, !memoryOnly, DateTimeOffset.UtcNow, token.ExpiresAt);
        return await LockedAsync(ct, async () =>
        {
            var existing = registry.Existing(id);
            if (existing is null && registry.Count >= AccountInfoValidator.AccountLimit)
                throw new AccountException(AccountFailureText.TooMany(AccountInfoValidator.AccountLimit));
            if (existing?.IsPersistent == true && memoryOnly)
                throw new AccountException(AccountFailureText.SavedStoreLocked);
            await vault.WriteAsync(account, token, ct).ConfigureAwait(false);
            // Once the credential is replaced, complete metadata publication even if the UI closes.
            if (account.IsPersistent)
            {
                try { await file.UpdateAsync(stored => [.. stored.Where(saved => saved.Id != id), account], CancellationToken.None).ConfigureAwait(false); }
                catch
                {
                    if (existing?.IsPersistent != true) await vault.DeleteSavedAsync(id, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
            if (existing is { IsPersistent: false } && account.IsPersistent) await vault.DeleteAsync(existing, CancellationToken.None).ConfigureAwait(false);
            registry.Replace(account);
            return account;
        }).ConfigureAwait(false);
    }
    public Task<AccountInfo> VerifyAsync(string accountId, CancellationToken ct) => LockedAsync(ct, async () =>
        {
            var account = registry.Find(accountId);
            var token = await CredentialAsync(account, ct).ConfigureAwait(false);
            var identity = await provider.IdentityAsync(account.Provider, account.ApiBaseUri, token.AccessToken, ct).ConfigureAwait(false);
            if (identity.Id != account.ProviderUserId) throw new AccountException(AccountFailureText.IdentityChanged);
            var updated = registry.Find(accountId) with { UserName = identity.UserName, VerifiedAt = DateTimeOffset.UtcNow };
            await UpdateAsync(updated, ct).ConfigureAwait(false);
            return updated;
        });

    public Task RemoveAsync(string accountId, CancellationToken ct) => LockedAsync(ct, async () =>
        {
            var account = registry.Find(accountId);
            try { await vault.DeleteAsync(account, ct).ConfigureAwait(false); }
            catch (SecretStoreException) { throw new AccountException(AccountFailureText.RemoveNeedsStore); }
            if (account.IsPersistent)
                await file.UpdateAsync(stored => stored.Where(saved => saved.Id != accountId).ToArray(), CancellationToken.None).ConfigureAwait(false);
            registry.Remove(accountId);
        });

    public Task<RemoteRepository> CreateRepositoryAsync(string accountId, string? ownerId, string name, bool isPrivate, CancellationToken ct) =>
        WithCredentialAsync(accountId, ct, (account, token) => provider.CreateRepositoryAsync(account.Provider, account.ApiBaseUri, token, ownerId, name, isPrivate, ct));

    private Task<T> WithCredentialAsync<T>(string accountId, CancellationToken ct, Func<AccountInfo, string, Task<T>> action) =>
        LockedAsync(ct, async () =>
        {
            var account = registry.Find(accountId);
            var token = await CredentialAsync(account, ct).ConfigureAwait(false);
            return await action(account, token.AccessToken).ConfigureAwait(false);
        });

    // The accounts Trag keeps; the credential store's status is asked only where signing in needs it.
    public Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct) => LockedAsync(ct, () => Task.FromResult<IReadOnlyList<AccountInfo>>(registry.All));

    public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct) =>
        WithCredentialAsync(accountId, ct, (account, token) => provider.Repositories(account.Provider, account.ApiBaseUri, token, ct));

    public Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) =>
        WithCredentialAsync(accountId, ct, (account, token) => provider.Owners(account.Provider, account.ApiBaseUri, token, ct));

    // Only the transport adapter receives a broker; the token never crosses to presentation.
    public Task<CredentialBroker> CreateBrokerAsync(string accountId, Uri endpoint, CancellationToken ct) => LockedAsync(ct, async () =>
        {
            var account = registry.Find(accountId);
            // The whole host, as submodules ask for it, is the whole instance for an account served under a path. Git asks
            // for a repository by its address without the slashes at its end.
            var path = endpoint.AbsolutePath.TrimEnd('/');
            var origin = new Uri(endpoint, path.Length == 0 ? AccountEndpoints.BasePath(account) + "/" : path);
            if (!AccountEndpoints.Matches(origin, account)) throw new AccountException(AccountFailureText.DestinationMismatch);
            var token = await CredentialAsync(account, ct).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(token.AccessToken);
            // Renewing the credential may have renewed the name with it, so the broker takes the account as it is now.
            try { return new CredentialBroker(origin, registry.Find(accountId).UserName, bytes, ct); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        });

    // Every operation runs alone, on the settings as the file has them now.
    private async Task<T> LockedAsync<T>(CancellationToken ct, Func<Task<T>> action)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadAsync(ct).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private Task LockedAsync(CancellationToken ct, Func<Task> action) => LockedAsync<object?>(ct, async () =>
    {
        await action().ConfigureAwait(false);
        return null;
    });

    private async Task<TokenData> CredentialAsync(AccountInfo account, CancellationToken ct) =>
        await refresh.ApplyAsync(account, await vault.ReadAsync(account, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    private async Task UpdateAsync(AccountInfo updated, CancellationToken ct)
    {
        if (updated.IsPersistent) await file.UpdateAsync(stored => stored.Select(saved => saved.Id == updated.Id ? updated : saved).ToArray(), ct).ConfigureAwait(false);
        registry.Update(updated);
    }
    // Every operation starts from the saved accounts as the file has them now: another instance of Trag may have changed
    // them, and each change is then made to the file as it is.
    private async Task LoadAsync(CancellationToken ct)
    {
        if (await file.LoadAsync(ct).ConfigureAwait(false) is { } stored) registry.ReplaceSaved(stored);
    }
    private static void RequireStorage(SecretStoreStatus status, bool memoryOnly)
    {
        if (!memoryOnly && status != SecretStoreStatus.Available) throw new AccountException(AccountFailureText.StoreNeeded);
    }
    public void Dispose() { vault.Dispose(); provider.Dispose(); }
}
