using System.Net;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.Infrastructure.Accounts.Providers;

// GitHub's addresses, JSON fields and refusals. An organization is named by its login, so a login Trag would have to
// escape into a path is not offered at all.
internal sealed class GitHubProvider(ProviderIdentityReader identities, ProviderRepositoryPages repositories,
    ProviderOwnerReader owners, ProviderRepositoryCreator creator) : IRepositoryProvider
{
    public Task<(string Id, string UserName)> IdentityAsync(Uri api, string token, CancellationToken ct) =>
        identities.ReadAsync(api, token, user => user.Login, ct);

    public Task<RemoteRepositoryList> Repositories(Uri api, string token, CancellationToken ct) =>
        repositories.ReadAsync(api, token,
            page => $"user/repos?per_page={ProviderRepositoryPages.PageSize}&sort=pushed&affiliation=owner,collaborator,organization_member&page={page}", Map, ct);

    public Task<IReadOnlyList<PublishOwner>> Owners(Uri api, string token, CancellationToken ct) =>
        owners.ReadAsync(api, token, page => $"user/orgs?per_page={ProviderRepositoryPages.PageSize}&page={page}", status => status == HttpStatusCode.Forbidden
            ? ProviderText.OrganizationsNotListed
            : null, Owner, ct);

    public Task<RemoteRepository> CreateRepositoryAsync(Uri api, string token, string? owner, string name, bool isPrivate, CancellationToken ct) =>
        creator.CreateAsync(api, token, name, "GitHub", validated =>
        {
            if (owner is not null && !ValidOwner(owner)) throw new AccountException(ProviderText.OwnerMissing);
            return (owner is null ? "user/repos" : "orgs/" + Uri.EscapeDataString(owner) + "/repos",
                new RepositoryCreation { Name = validated, Private = isPrivate, AutoInit = false });
        }, ProviderText.GitHubCreateDenied, Map, ct);

    private static PublishOwner? Owner(ProviderOwner item)
    {
        var owner = ProviderOwnerReader.Owner(item.Login, item.Login);
        return owner is not null && ValidOwner(owner.Id) ? owner : null;
    }

    private static RemoteRepository? Map(ProviderRepository item) =>
        ProviderRepositoryMapper.Map(item.FullName, item.CloneUrl, item.HtmlUrl, item.DefaultBranch, item.Private, item.PushedAt ?? item.UpdatedAt);

    private static bool ValidOwner(string owner) => owner.Length is > 0 and <= 100 && owner.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
