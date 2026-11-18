using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;

namespace Trag.Application.Ports.Catalog;

public interface IRemoteRepositoryDirectory
{
    Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct);
    Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct);
    // Organizations or groups the account can create repositories in; the account user itself is implied.
    Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct);
}
