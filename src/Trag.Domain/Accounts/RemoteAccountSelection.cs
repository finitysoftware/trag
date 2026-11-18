using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Accounts;

// Which account a remote is used with, and the sentence that says why.
public static class RemoteAccountSelection
{
    public static RemoteAccountState Choose(IReadOnlyList<AccountInfo> accounts, string fetch, string push, string? savedId)
    {
        var endpoints = new[] { fetch, push }.Select(RemoteAddress.Https).Where(u => u is not null).Cast<Uri>().ToArray();
        var ssh = RemoteAddress.IsSsh(fetch) || RemoteAddress.IsSsh(push);
        var candidates = MatchingAccounts(accounts, endpoints, ssh);
        var choices = AccountChoice.ForAccounts(candidates);
        var id = SelectedId(candidates, choices, endpoints.Length != 0, savedId);
        return new(choices, id, Message(id, ssh, savedId, candidates));
    }

    private static AccountInfo[] MatchingAccounts(IReadOnlyList<AccountInfo> accounts, Uri[] endpoints, bool ssh)
    {
        if (endpoints.Length != 0)
            return accounts.Where(account => endpoints.All(endpoint => AccountEndpoints.Matches(endpoint, account))).ToArray();
        return ssh ? accounts.ToArray() : [];
    }

    private static string SelectedId(AccountInfo[] candidates, IReadOnlyList<AccountChoice> choices, bool https, string? savedId)
    {
        // A missing saved account falls back to Git; it must not silently select another identity.
        if (savedId is not null) return choices.Any(choice => choice.Id == savedId) ? savedId : AccountChoice.External;
        if (https && candidates.Length == 1) return candidates[0].Id;
        return AccountChoice.External;
    }

    private static string Message(string id, bool ssh, string? savedId, AccountInfo[] candidates)
    {
        if (id == AccountChoice.External) return AccountText.RemoteUsesGit;
        if (ssh) return AccountText.RemoteUsesSshKey;
        if (savedId is not null) return AccountText.RemoteUsesSaved;
        return AccountText.RemoteUsesOnlyMatch(candidates[0]);
    }
}
