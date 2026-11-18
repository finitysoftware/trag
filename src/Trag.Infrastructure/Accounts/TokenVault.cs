using System.Security.Cryptography;
using System.Text.Json;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts.Providers;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Infrastructure.Accounts;

// Where a credential lives: the OS credential store for a saved account, memory for a session-only one. The account
// itself decides which, so nothing else has to. Serialized bytes are cleared as soon as the store has them.
internal sealed class TokenVault(ISecretStore vault) : IDisposable
{
    private readonly MemorySecretStore memory = new();

    private ISecretStore Store(AccountInfo account) => account.IsPersistent ? vault : memory;

    public Task<SecretStoreStatus> StatusAsync(CancellationToken ct) => vault.GetStatusAsync(ct);

    // Three things can be wrong with a stored credential, and each is its own sentence: the store cannot be reached, the
    // credential is not there, or what is there is no longer a token. None of them is something the person can retype.
    public async Task<TokenData> ReadAsync(AccountInfo account, CancellationToken ct)
    {
        byte[]? bytes;
        try { bytes = await Store(account).ReadAsync(account.Id, ct).ConfigureAwait(false); }
        catch (SecretStoreException) { throw new AccountException(AccountFailureText.StoreLocked); }
        if (bytes is null) throw new AccountException(AccountFailureText.CredentialMissing);
        var token = Deserialize(bytes);
        if (token is null || !TokenValidation.IsValid(token.AccessToken)) throw new AccountException(AccountFailureText.CredentialUnreadable);
        return token;
    }

    public async Task WriteAsync(AccountInfo account, TokenData token, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(token, AccountJsonContext.Default.TokenData);
        try { await Store(account).WriteAsync(account.Id, bytes, ct).ConfigureAwait(false); }
        catch (SecretStoreException) { throw new AccountException(AccountFailureText.CredentialNotSaved); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public Task DeleteAsync(AccountInfo account, CancellationToken ct) => Store(account).DeleteAsync(account.Id, ct);

    // Used to undo a write to the OS store when the metadata that would have described it could not be saved.
    public Task DeleteSavedAsync(string accountId, CancellationToken ct) => vault.DeleteAsync(accountId, ct);

    public void Dispose() => memory.Dispose();

    private static TokenData? Deserialize(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize(bytes, AccountJsonContext.Default.TokenData); }
        catch (JsonException) { return null; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
