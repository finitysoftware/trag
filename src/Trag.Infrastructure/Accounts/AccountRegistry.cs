using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Accounts;

// The accounts this session knows about, saved and session-only together. Nothing here reaches a store or a file; the
// service holds the gate that makes these calls safe.
internal sealed class AccountRegistry
{
    private readonly List<AccountInfo> accounts = [];

    public int Count => accounts.Count;
    public AccountInfo[] All => accounts.ToArray();

    public AccountInfo? Existing(string id) => accounts.FirstOrDefault(a => a.Id == id);

    public AccountInfo Find(string id) => Existing(id) ?? throw new AccountException(AccountFailureText.AccountRemoved);

    // The saved accounts as the settings file has them now; one this session keeps only for itself stays as it is.
    public void ReplaceSaved(IEnumerable<AccountInfo> stored)
    {
        accounts.RemoveAll(a => a.IsPersistent);
        accounts.AddRange(stored.Where(account => Existing(account.Id) is null).ToArray());
    }

    public void Replace(AccountInfo account)
    {
        accounts.RemoveAll(a => a.Id == account.Id);
        accounts.Add(account);
    }

    public void Remove(string id) => accounts.RemoveAll(a => a.Id == id);

    // Every account operation runs alone and finds its account first, so the account is still here.
    public void Update(AccountInfo updated) => accounts[accounts.FindIndex(a => a.Id == updated.Id)] = updated;
}
