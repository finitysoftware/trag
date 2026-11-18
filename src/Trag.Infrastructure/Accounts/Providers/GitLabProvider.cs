using System.Globalization;
using System.Net;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.Infrastructure.Accounts.Providers;

// GitLab's addresses, JSON fields and refusals. A group is named by its numeric id, and a project's visibility is a
// word rather than a flag, so anything GitLab may add later is shown as unknown rather than as public.
internal sealed class GitLabProvider(ProviderIdentityReader identities, ProviderRepositoryPages repositories,
    ProviderOwnerReader owners, ProviderRepositoryCreator creator) : IRepositoryProvider
{
    public Task<(string Id, string UserName)> IdentityAsync(Uri api, string token, CancellationToken ct) =>
        identities.ReadAsync(api, token, user => user.Username, ct);

    public Task<RemoteRepositoryList> Repositories(Uri api, string token, CancellationToken ct) =>
        repositories.ReadAsync(api, token,
            page => $"projects?membership=true&per_page={ProviderRepositoryPages.PageSize}&order_by=last_activity_at&sort=desc&page={page}", Map, ct);

    public Task<IReadOnlyList<PublishOwner>> Owners(Uri api, string token, CancellationToken ct) =>
        owners.ReadAsync(api, token, page => $"groups?min_access_level=30&per_page={ProviderRepositoryPages.PageSize}&order_by=path&sort=asc&page={page}",
            status => status == HttpStatusCode.Forbidden
                ? ProviderText.GroupsNotListed
                : null, Owner, ct);

    public Task<RemoteRepository> CreateRepositoryAsync(Uri api, string token, string? owner, string name, bool isPrivate, CancellationToken ct) =>
        creator.CreateAsync(api, token, name, "GitLab", validated =>
        {
            long? group = null;
            if (owner is not null)
            {
                if (!long.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) throw new AccountException(ProviderText.GroupMissing);
                group = id;
            }
            return ("projects", new RepositoryCreation { Name = validated, Path = validated, Visibility = isPrivate ? "private" : "public", NamespaceId = group, InitializeWithReadme = false });
        }, ProviderText.GitLabCreateDenied, Map, ct);

    private static PublishOwner? Owner(ProviderOwner item) =>
        ProviderOwnerReader.Owner(item.Id?.ToString(CultureInfo.InvariantCulture), item.FullPath);

    private static RemoteRepository? Map(ProviderRepository item) =>
        ProviderRepositoryMapper.Map(item.PathWithNamespace, item.HttpUrlToRepo, item.WebUrl, item.DefaultBranch,
            item.Visibility switch { "private" => true, "public" or "internal" => false, _ => null }, item.LastActivityAt);
}
