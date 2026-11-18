using Trag.Application.Ports.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;

namespace Trag.Application.Accounts;

// The accounts as their hosts list them, beyond any one repository: all of them in display order, the repositories each
// hosts, and the owners one of them can publish under. Edited says the user finished editing them (Settings closed), so
// what read them may read again.
public sealed class AccountsModel(IRemoteRepositoryDirectory directory)
{
    public event Action? Edited;

    public void EndEditing() => Edited?.Invoke();

    public async Task<IReadOnlyList<AccountInfo>> ListAsync(CancellationToken ct) =>
        AccountInfo.InDisplayOrder(await directory.AccountsAsync(ct));

    // The accounts, or null when they cannot be read, which the caller says while it goes on; a cancellation stops it.
    public async Task<IReadOnlyList<AccountInfo>?> TryListAsync(CancellationToken ct)
    {
        try { return await ListAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    public Task<RemoteRepositoryList> RepositoriesAsync(AccountInfo account, CancellationToken ct) =>
        directory.ListRemoteAsync(account.Id, ct);

    // The organizations the account can publish under, beside the account itself.
    public Task<IReadOnlyList<PublishOwner>> OwnersAsync(AccountInfo account, CancellationToken ct) =>
        directory.OwnersAsync(account.Id, ct);
}
