namespace Trag.Domain.Accounts;

// What kind of address a remote carries. Only a clean HTTPS URL can be matched against an account; anything that
// looks like SSH is Git's own business, key and agent included.
public static class RemoteAddress
{
    public static Uri? Https(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri : null;
    // What Git or a browser is handed: HTTPS with no credentials in it.
    public static Uri? HttpsWithoutCredentials(string? value) => Https(value) is { UserInfo.Length: 0 } uri ? uri : null;

    public static bool IsSsh(string value) => value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) || IsScpLike(value);

    // A project's path on its host as an address writes it: no slashes around it, and no ".git" ending.
    public static string ProjectPath(string path)
    {
        path = path.Trim('/');
        return path.EndsWith(".git", StringComparison.Ordinal) ? path[..^4] : path;
    }

    // Git's [user@]host:path: the colon comes before any slash, and one letter before it is a Windows drive.
    public static bool IsScpLike(string value) => !value.Contains("://", StringComparison.Ordinal) && value.IndexOf(':') is var colon and > 1
        && value.IndexOfAny(['/', '\\']) is var slash && (slash < 0 || slash > colon);
}
