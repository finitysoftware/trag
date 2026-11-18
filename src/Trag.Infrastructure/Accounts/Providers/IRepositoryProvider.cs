using Trag.Domain.Catalog;
using Trag.Domain.Publish;

namespace Trag.Infrastructure.Accounts.Providers;

// What a hosting provider can answer about an account, once the account already knows which provider it belongs to.
// Each implementation owns its own addresses, JSON fields and refusal sentences.
internal interface IRepositoryProvider
{
    Task<(string Id, string UserName)> IdentityAsync(Uri api, string token, CancellationToken ct);
    Task<RemoteRepositoryList> Repositories(Uri api, string token, CancellationToken ct);
    Task<IReadOnlyList<PublishOwner>> Owners(Uri api, string token, CancellationToken ct);
    Task<RemoteRepository> CreateRepositoryAsync(Uri api, string token, string? owner, string name, bool isPrivate, CancellationToken ct);
}
