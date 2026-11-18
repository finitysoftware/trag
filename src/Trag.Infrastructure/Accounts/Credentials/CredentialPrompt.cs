namespace Trag.Infrastructure.Accounts.Credentials;

internal sealed record CredentialPrompt(Uri Destination, bool IsPassword)
{
    public static CredentialPrompt? Parse(string text)
    {
        var username = text.StartsWith("Username for '", StringComparison.Ordinal);
        var password = text.StartsWith("Password for '", StringComparison.Ordinal);
        if (!username && !password) return null;
        if (!text.EndsWith("': ", StringComparison.Ordinal)) return null;
        var address = text[(text.IndexOf('\'') + 1)..^3];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != "https" || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        return new(uri, password);
    }

    // The repository the prompt asks for on its host: Git's username and password prompts for one address are one.
    public string Repository => Destination.AbsolutePath;

    public bool Matches(Uri origin, string userName)
    {
        if (!string.Equals(Destination.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase)) return false;
        if (Destination.Port != origin.Port) return false;
        // Repository brokers use an exact path; an instance's broker, its path ending in a slash (a host-only probe passes
        // the root), answers for every path under it.
        if (origin.AbsolutePath.EndsWith('/') ? !Destination.AbsolutePath.StartsWith(origin.AbsolutePath, StringComparison.Ordinal)
            : Destination.AbsolutePath != origin.AbsolutePath) return false;
        return IsPassword ? Uri.UnescapeDataString(Destination.UserInfo) == userName : Destination.UserInfo.Length == 0;
    }
}
