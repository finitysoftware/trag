namespace Trag.Domain.Accounts;

// What an account entry on disk has to be before Trag uses it: saved rather than session-only, clean HTTPS on both
// addresses, an id that matches what those addresses would produce, and a name that is one.
public static class AccountInfoValidator
{
    // How many accounts Trag keeps. The service refuses to add past this and the settings file refuses to load past it,
    // so the two have to agree; the sentence the user reads names the same number.
    public const int AccountLimit = 100;

    public static bool Valid(AccountInfo a) => a.IsPersistent && Enum.IsDefined(a.Provider)
        && AccountEndpoints.IsClean(a.ApiBaseUri) && AccountEndpoints.IsClean(a.TransportOrigin)
        && a.Id == AccountIdentity.StableId(a.Provider, a.ApiBaseUri, a.ProviderUserId)
        && (a.Provider == AccountProvider.GitHub ? a.ApiBaseUri == AccountMapping.GitHubApi && a.TransportOrigin == AccountMapping.GitHubOrigin
            : AccountMapping.GitLabApiBase(a.TransportOrigin.AbsoluteUri) == a.ApiBaseUri)
        && AccountName.IsValid(a.UserName);
}
