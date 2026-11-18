using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Publish;

// The accounts a repository can be published from, and the owners of the one chosen: two reads with separate lifetimes
// over the application's accounts, each registered with the repository's Cancel. The owners read of an account shows
// the account itself while its organizations load.
public sealed class PublishAccountsModel
{
    private readonly AccountsModel accounts;

    public PublishAccountsModel(AccountsModel accounts, Repository repository)
    {
        this.accounts = accounts;
        All = new([], repository.Cancels) { FailedMessage = PublishText.AccountsNotRead };
        Owners = new(PublishOwnerList.ForAccount(null), repository.Cancels)
        {
            FailedMessage = PublishText.OwnersNotListed,
            DescribeFailure = error => (error as AccountException)?.Message
        };
    }

    public Loader<IReadOnlyList<AccountInfo>> All { get; }
    public Loader<PublishOwnerList> Owners { get; }
    public bool IsBusy => All.IsBusy || Owners.IsBusy;
    // The accounts read speaks first; the owners read only when the accounts arrived.
    public string Message => All.Message.Length != 0 ? All.Message : Owners.Message;

    // A repository is created from a chosen account with an owner chosen among its owners, once nothing is read anymore.
    public bool CanCreate(AccountInfo? account) => account is not null && Owners.Value.Selected is not null && !IsBusy;

    public Task ReadAccountsAsync() => All.LoadAsync(async ct => await accounts.ListAsync(ct));

    // The owners of the chosen account; no account, or no directory to ask, leaves none.
    public Task ReadOwnersAsync(AccountInfo? account)
    {
        if (account is null)
        {
            Owners.Clear();
            return Task.CompletedTask;
        }
        return Owners.LoadAsync(async ct => PublishOwnerList.ForAccount(account).WithOrganizations(await accounts.OwnersAsync(account, ct)),
            interim: PublishOwnerList.ForAccount(account));
    }

    public void Cancel()
    {
        All.Cancel();
        Owners.Cancel();
    }

    public void Clear()
    {
        All.Clear();
        Owners.Clear();
    }
}
