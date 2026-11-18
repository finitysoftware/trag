using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;

namespace Trag.Infrastructure.Accounts.Providers;

// The provider side of the account service: it owns the one HTTP client, the GitHub sign-in and the two providers,
// and routes each call to the provider the account belongs to.
public sealed class ProviderClient : IDisposable
{
    private readonly ProviderHttpClient http;
    private readonly GitHubDeviceLoginClient logins;
    private readonly GitHubTokenClient tokens;
    private readonly GitHubProvider github;
    private readonly GitLabProvider gitlab;

    public ProviderClient(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        var time = clock ?? TimeProvider.System;
        http = new(handler);
        tokens = new(http, time);
        logins = new(tokens, time);
        var identities = new ProviderIdentityReader(http);
        var pages = new ProviderRepositoryPages(http);
        var owners = new ProviderOwnerReader(http);
        var creator = new ProviderRepositoryCreator(http);
        github = new(identities, pages, owners, creator);
        gitlab = new(identities, pages, owners, creator);
    }

    internal Task<TokenData> DeviceLoginAsync(IProgress<DeviceLoginProgress> progress, CancellationToken ct) =>
        logins.DeviceLoginAsync(progress, ct);

    internal Task<TokenData> RefreshAsync(TokenData token, CancellationToken ct) => tokens.RefreshAsync(token, ct);

    internal Task<(string Id, string UserName)> IdentityAsync(AccountProvider provider, Uri api, string token, CancellationToken ct) =>
        For(provider).IdentityAsync(api, token, ct);

    internal Task<RemoteRepositoryList> Repositories(AccountProvider provider, Uri api, string token, CancellationToken ct) =>
        For(provider).Repositories(api, token, ct);

    internal Task<IReadOnlyList<PublishOwner>> Owners(AccountProvider provider, Uri api, string token, CancellationToken ct) =>
        For(provider).Owners(api, token, ct);

    internal Task<RemoteRepository> CreateRepositoryAsync(AccountProvider provider, Uri api, string token, string? owner, string name, bool isPrivate, CancellationToken ct) =>
        For(provider).CreateRepositoryAsync(api, token, owner, name, isPrivate, ct);

    private IRepositoryProvider For(AccountProvider provider) => provider == AccountProvider.GitHub ? github : gitlab;

    public void Dispose() => http.Dispose();
}
