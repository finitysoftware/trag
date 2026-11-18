using System.Security.Cryptography;
using System.Text;

namespace Trag.Domain.Accounts;

// The one identifier an account is known by, everywhere: derived from the provider, its API address and the user id
// the provider gave, so the same person on two instances is two accounts and the same account survives a rename.
public static class AccountIdentity
{
    public static string StableId(AccountProvider provider, Uri api, string userId) =>
        "account-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + "\n" + api.AbsoluteUri + "\n" + userId))).ToLowerInvariant();
}
