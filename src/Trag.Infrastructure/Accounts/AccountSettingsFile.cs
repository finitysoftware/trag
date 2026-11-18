using System.Text.Json;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.Accounts;

// The account metadata on disk. No credential is ever written here. The file is read whole or not at all, and it is
// replaced rather than edited, so a failed write leaves the previous file untouched. What a malformed or oversized
// file means to the user is decided here; the reading and the replacement themselves are the shared ones.
internal sealed class AccountSettingsFile(string? settingsPath)
{
    private readonly JsonFileStore<AccountDocument>? file = settingsPath is null ? null
        : new(settingsPath, AccountJsonContext.Default.AccountDocument, 262_144);

    // The saved accounts; null when the session keeps no settings file at all.
    public async Task<AccountInfo[]?> LoadAsync(CancellationToken ct)
    {
        if (file is null) return null;
        if (!File.Exists(settingsPath)) return [];
        try { return Valid(await file.LoadAsync(ct).ConfigureAwait(false) ?? throw new JsonException()); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new AccountException(AccountFailureText.SettingsNotRead); }
    }

    // A change made to the saved accounts as the file has them now, which another instance of Trag may have changed.
    public async Task UpdateAsync(Func<AccountInfo[], AccountInfo[]> change, CancellationToken ct)
    {
        if (file is null) return;
        try { await file.UpdateAsync(document => new(change(document is null ? [] : Valid(document))), ct).ConfigureAwait(false); }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { throw new AccountException(AccountFailureText.SettingsNotRead); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new AccountException(AccountFailureText.SettingsNotSaved); }
    }

    // Held while a credential is renewed, so another instance of Trag renewing the same one waits for it; a session
    // without a settings file shares its credentials with no other.
    public async Task<IAsyncDisposable?> RenewalLockAsync(CancellationToken ct) =>
        settingsPath is null ? null : await FileLock.AcquireAsync(settingsPath + ".renewal.lock", ct).ConfigureAwait(false);

    private static AccountInfo[] Valid(AccountDocument document)
    {
        if (document.Accounts.Length > AccountInfoValidator.AccountLimit || document.Accounts.Any(a => a is null || !AccountInfoValidator.Valid(a))
            || document.Accounts.DistinctBy(a => a.Id).Count() != document.Accounts.Length) throw new JsonException();
        return document.Accounts;
    }
}
