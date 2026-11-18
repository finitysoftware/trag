namespace Trag.Domain.Naming;

public static class RemoteUrls
{
    // Git also accepts SSH syntax and local paths, so this checks one argument, not just URI syntax.
    public static bool IsValidAddress(string? address) => address is not null && RefNames.HasSafeCharacters(address.Trim(), GitLimits.AddressLength);

    // A password or a token in an address, which Git would keep in .git/config in plain text: a user with a password, or a
    // token as the user name.
    public static bool HasCredentials(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.UserInfo.Contains(':') || TokenPrefixes.Any(prefix => uri.UserInfo.StartsWith(prefix, StringComparison.Ordinal)));

    // The address with its password or token left out, for showing it; a user name alone stays, as Git's credential
    // helpers pick the account by it. The user part ends at the last '@' before the address's path.
    public static string WithoutCredentials(string url)
    {
        if (!HasCredentials(url)) return url;
        var address = url.Trim();
        var start = address.IndexOf("://", StringComparison.Ordinal) + 3;
        var end = address.IndexOfAny(['/', '?', '#'], start) is var path and >= 0 ? path : address.Length;
        var at = address.LastIndexOf('@', end - 1, end - start);
        var user = address[start..at].Split(':')[0];
        var kept = user.Length != 0 && !TokenPrefixes.Any(prefix => user.StartsWith(prefix, StringComparison.Ordinal)) ? user + "@" : "";
        return address[..start] + kept + address[(at + 1)..];
    }

    // The prefixes GitHub and GitLab give their tokens.
    private static readonly string[] TokenPrefixes =
        ["ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_", "glpat-", "gloas-", "gldt-", "glrt-", "glptt-", "glft-", "glimt-", "glsoat-"];

    // Compares "https://github.com/Owner/Repo.git", "git@github.com:owner/repo" and "ssh://git@github.com/owner/repo/" as one repository.
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var value = url.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            var at = value.IndexOf('@');
            var colon = value.IndexOf(':');
            if (at >= 0 && colon > at) value = value[(at + 1)..colon] + "/" + value[(colon + 1)..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort || uri.Port <= 0 ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            value = uri.IdnHost + port + uri.AbsolutePath;
        }
        value = value.Trim('/').Replace('\\', '/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value.TrimEnd('/').ToLowerInvariant();
    }

    public static string? Owner(string? url)
    {
        var parts = Normalize(url).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[^2] : null;
    }

    public static string? Name(string? url)
    {
        var parts = Normalize(url).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }
}
