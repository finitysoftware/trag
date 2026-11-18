using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Catalog;

// One account's hosted repositories: one Loader for the list, whose message says what its read did. Registered for
// cancelling, the shell's Cancel reaches the read; Cancel is the catalog's, when the shell goes, and Release when the
// account does, so the read leaves the shell's Cancel with it.
public sealed partial class HostedRepositories : ObservableObject
{
    private readonly AccountsModel accounts;

    public HostedRepositories(AccountsModel accounts, AccountInfo account, CancelRegistry cancels)
    {
        this.accounts = accounts;
        Account = account;
        List = new(empty: [], cancels)
        {
            CanceledMessage = CatalogText.GroupCanceled,
            FailedMessage = CatalogText.GroupFailed,
            DescribeFailure = error => (error as AccountException)?.Message
        };
    }

    public AccountInfo Account { get; }
    public Loader<IReadOnlyList<RemoteRepository>> List { get; }
    // An answer arrived, an empty one included: an empty answer is the same empty list as before it, so the read notes
    // the answer itself.
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    // Whether the last answer read every page Trag reads. Set before the list takes the answer's rows, so what reads it with the
    // rows reads the same answer.
    public bool ReachedLimit { get; private set; }

    public Task LoadAsync() => List.LoadAsync(async ct =>
    {
        var answer = await accounts.RepositoriesAsync(Account, ct);
        ct.ThrowIfCancellationRequested();
        IsLoaded = true;
        ReachedLimit = answer.ReachedLimit;
        return answer.Repositories.ToArray();
    });

    public void Cancel() => List.Cancel();

    public void Release()
    {
        List.Cancel();
        List.Release();
    }
}
