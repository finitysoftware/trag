using Trag.Domain.Accounts;

namespace Trag.Domain.Publish;

// The namespaces an account may publish into: the account itself first, then its organizations once each, sorted by
// name. Without an account there is nothing to publish into.
public static class PublishOwners
{
    public static IReadOnlyList<PublishOwner> ForAccount(AccountInfo? account) =>
        account is null ? [] : [new(account.UserName, account.UserName, true)];

    public static IReadOnlyList<PublishOwner> With(IReadOnlyList<PublishOwner> owners, IEnumerable<PublishOwner> organizations)
    {
        if (owners.Count == 0)
            return owners;
        return [owners[0], .. organizations.Where(IsOrganization).DistinctBy(owner => owner.Id).OrderBy(owner => owner.Display, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsOrganization(PublishOwner owner) => !owner.IsUser && owner.Id.Length != 0;
}
