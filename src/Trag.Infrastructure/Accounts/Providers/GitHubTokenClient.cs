using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Providers;

// GitHub's OAuth endpoint and what comes back from it. A token is accepted only as a bearer token that carries the
// repository permission; nothing else is worth storing.
internal sealed class GitHubTokenClient(ProviderHttpClient http, TimeProvider clock)
{
    public const string TokenUrl = "https://github.com/login/oauth/access_token";

    public async Task<OAuthResponse> OAuthAsync(string url, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        return await http.JsonAsync(request, ProviderJsonContext.Default.OAuthResponse, SignInText.UnreadableResponse, ct).ConfigureAwait(false)
            ?? throw new AccountException(SignInText.EmptyResponse);
    }

    public async Task<TokenData> RefreshAsync(TokenData token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.RefreshToken) || string.IsNullOrEmpty(token.ClientId)
            || token.RefreshExpiresAt <= clock.GetUtcNow()) throw new AccountException(SignInText.NotRenewable);
        var response = await OAuthAsync(TokenUrl, new()
        { ["client_id"] = token.ClientId, ["refresh_token"] = token.RefreshToken, ["grant_type"] = "refresh_token" }, ct).ConfigureAwait(false);
        if (response.Error is not null) throw OAuthErrorMessages.ExplainRenewal(response);
        return Tokens(response, token.ClientId);
    }

    public TokenData Tokens(OAuthResponse response, string clientId)
    {
        TokenValidation.Validate(response.AccessToken);
        if (!string.Equals(response.TokenType, "bearer", StringComparison.OrdinalIgnoreCase)) throw new AccountException(SignInText.UnsupportedTokenType);
        if (!(response.Scope ?? "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Contains("repo", StringComparer.Ordinal))
            throw new AccountException(SignInText.NoRepositoryAccess);
        if (response.RefreshToken is not null) TokenValidation.Validate(response.RefreshToken);
        return new() { AccessToken = response.AccessToken!, RefreshToken = response.RefreshToken, ClientId = clientId,
            ExpiresAt = response.ExpiresIn > 0 ? clock.GetUtcNow().AddSeconds(response.ExpiresIn) : null,
            RefreshExpiresAt = response.RefreshTokenExpiresIn > 0 ? clock.GetUtcNow().AddSeconds(response.RefreshTokenExpiresIn) : null };
    }
}
