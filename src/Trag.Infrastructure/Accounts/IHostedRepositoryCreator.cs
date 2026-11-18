using Trag.Domain.Catalog;

namespace Trag.Infrastructure.Accounts;

// Creates an empty repository on the hosting site an account belongs to, so publishing has somewhere to push.
public interface IHostedRepositoryCreator
{
    Task<RemoteRepository> CreateRepositoryAsync(string accountId, string? ownerId, string name, bool isPrivate, CancellationToken ct);
}
