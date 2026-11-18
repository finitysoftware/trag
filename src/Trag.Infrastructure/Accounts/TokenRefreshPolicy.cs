using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts.Providers;

namespace Trag.Infrastructure.Accounts;

// What has to happen before a credential may be used: a token near its expiry is renewed, and a renewed token is only
// trusted once the provider confirms it still belongs to the same person. GitHub accepts a refresh token once, so a
// renewal holds the renewal lock and starts from the token as it is stored then: another instance of Trag may have
// renewed it meanwhile.
internal sealed class TokenRefreshPolicy(ProviderClient provider, TokenVault vault, Func<CancellationToken, Task<IAsyncDisposable?>> renewal,
    Func<AccountInfo, CancellationToken, Task> update)
{
    public async Task<TokenData> ApplyAsync(AccountInfo account, TokenData token, CancellationToken ct)
    {
        if (Expiring(token))
        {
            await using var held = await renewal(ct).ConfigureAwait(false);
            token = await vault.ReadAsync(account, ct).ConfigureAwait(false);
            if (Expiring(token))
            {
                token = await provider.RefreshAsync(token, ct).ConfigureAwait(false);
                token.RequiresIdentityVerification = true;
                // Store rotated secrets before further network work. Never retain a revoked refresh token on disk.
                await vault.WriteAsync(account, token, CancellationToken.None).ConfigureAwait(false);
            }
        }
        if (token.RequiresIdentityVerification)
        {
            var identity = await provider.IdentityAsync(account.Provider, account.ApiBaseUri, token.AccessToken, ct).ConfigureAwait(false);
            if (identity.Id != account.ProviderUserId) throw new AccountException(AccountFailureText.RenewedForOther);
            token.RequiresIdentityVerification = false;
            await vault.WriteAsync(account, token, CancellationToken.None).ConfigureAwait(false);
            await update(account with { UserName = identity.UserName, ExpiresAt = token.ExpiresAt, VerifiedAt = DateTimeOffset.UtcNow }, ct).ConfigureAwait(false);
        }
        return token;
    }

    private static bool Expiring(TokenData token) => token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1);
}
