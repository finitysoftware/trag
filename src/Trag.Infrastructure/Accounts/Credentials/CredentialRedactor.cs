using System.Text;

namespace Trag.Infrastructure.Accounts.Credentials;

// Removes this operation's credential from anything Git printed, in both forms it can appear in: the token itself,
// and the basic authentication value a server may echo back.
internal sealed class CredentialRedactor(string userName, byte[] secret)
{
    // Git's own credentials written into an address, user:password@, are hidden the same way; an address without a
    // password has nothing to hide.
    public static CredentialRedactor? ForAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.UserInfo.Split(':', 2) is [var user, { Length: > 0 } password]
            ? new(Uri.UnescapeDataString(user), Encoding.UTF8.GetBytes(Uri.UnescapeDataString(password))) : null;

    public string Redact(string text)
    {
        var token = Encoding.UTF8.GetString(secret);
        return text.Replace(token, "[credential]", StringComparison.Ordinal)
            .Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(userName + ":" + token)), "[credential]", StringComparison.Ordinal);
    }
}
