using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Accounts;

public sealed record AccountChoice(string Id, string Display)
{
    // Git resolves its own credential helper or SSH identity for this choice.
    public const string External = "external";

    // The Trag account a choice names; Git's own credentials name none.
    public static string? TragAccount(string? id) => id is External ? null : id;

    // The choice a list starts with: the earlier one while it is still offered, else the only matching account, else
    // Git's own credentials — with several matching accounts Git keeps its own until the user picks one.
    public static AccountChoice? Default(IReadOnlyList<AccountChoice> choices, string? previousId) =>
        choices.FirstOrDefault(choice => choice.Id == previousId) ?? (choices.Count == 2 ? choices[1] : choices.FirstOrDefault());

    public static IReadOnlyList<AccountChoice> ForAccounts(IEnumerable<AccountInfo> accounts) =>
        [new(External, AccountText.GitCredentials), .. accounts.Select(account => new AccountChoice(account.Id, account.Display))];

    // The choices for a remote address: none when no account matches its endpoint, otherwise Git's own credentials
    // followed by every account that does. Only a clean HTTPS address can match.
    public static IReadOnlyList<AccountChoice> ForUrl(string url, IReadOnlyList<AccountInfo> accounts)
    {
        if (RemoteAddress.Https(url.Trim()) is not { } endpoint) return [];
        var matches = accounts.Where(account => AccountEndpoints.Matches(endpoint, account)).ToArray();
        return matches.Length == 0 ? [] : ForAccounts(matches);
    }
}
