using Trag.Domain.Naming;

namespace Trag.Domain.Accounts;

// What a user name has to be wherever one is accepted: in a provider's identity response, in the settings file, and as
// the name a credential broker answers Git with. A blank, an over-long or a control-carrying value is not a name.
public static class AccountName
{
    public static bool IsValid(string? name) => PlainNames.IsValid(name, 200);
}
