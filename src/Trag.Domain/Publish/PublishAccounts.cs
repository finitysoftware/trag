using Trag.Domain.Accounts;

namespace Trag.Domain.Publish;

// Which provider the publish dialog opens on and which accounts it lists for it: GitHub first unless only GitLab
// accounts are there.
public static class PublishAccounts
{
    public static AccountProvider DefaultProvider(IReadOnlyList<AccountInfo> accounts) =>
        Has(accounts, AccountProvider.GitHub) || !Has(accounts, AccountProvider.GitLab) ? AccountProvider.GitHub : AccountProvider.GitLab;

    public static bool Has(IReadOnlyList<AccountInfo> accounts, AccountProvider provider) => accounts.Any(account => account.Provider == provider);
}
