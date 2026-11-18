namespace Trag.Domain.Accounts;

// The addresses an account talks to. GitHub's are fixed. A GitLab instance's API is derived from the address the person
// entered: the instance root with a trailing slash, and its REST API underneath; the origin is that root again. The same
// derivation checks a saved entry, so the two cannot drift apart.
public static class AccountMapping
{
    public static readonly Uri GitHubApi = new("https://api.github.com/");
    public static readonly Uri GitHubOrigin = new("https://github.com/");
    // Where GitHub's device sign-in takes its code, the only page a device code may name, and the GitLab instance an
    // account is added for unless another is typed.
    public const string GitHubDevicePage = "https://github.com/login/device";
    public const string GitLabDefault = "https://gitlab.com";

    public static Uri GitLabApiBase(string instanceUrl)
    {
        if (!Uri.TryCreate(instanceUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || !AccountEndpoints.IsClean(uri))
            throw new ArgumentException("GitLab requires an HTTPS instance URL without credentials, query or fragment.");
        return new(uri, "api/v4/");
    }

    public static Uri GitLabOrigin(Uri api) => new(api, "../../");
}
