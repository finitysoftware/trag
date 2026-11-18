namespace Trag.Domain.Accounts;

// What the account service read: the connected accounts and the state of the secret store.
public sealed record AccountSettings(IReadOnlyList<AccountInfo> Accounts, SecretStoreStatus VaultStatus)
{
    // The connected accounts of one provider, as the settings list them under it.
    public IReadOnlyList<AccountInfo> Of(AccountProvider provider) => AccountInfo.OfProvider(Accounts, provider);
}
