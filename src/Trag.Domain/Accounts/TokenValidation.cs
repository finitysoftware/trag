using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Accounts;

// The shape every access token must have: printable ASCII, no spaces, no line breaks, and short enough to be a token
// rather than a pasted page. It is checked where a token enters (typed in, or returned by GitHub), where one leaves a
// store, and where one goes on the wire or to Git.
public static class TokenValidation
{
    public const int MaximumLength = 1024;

    public static bool IsValid(string? token) => token is not null && IsValid(token.AsSpan());
    public static bool IsValid(ReadOnlySpan<char> token) => token.Length is > 0 and <= MaximumLength && token.IndexOfAnyExceptInRange('!', '~') < 0;
    public static bool IsValid(ReadOnlySpan<byte> token) => token.Length is > 0 and <= MaximumLength && token.IndexOfAnyExceptInRange((byte)'!', (byte)'~') < 0;

    public static void Validate(string? token)
    {
        if (!IsValid(token)) throw new AccountException(AccountFailureText.TokenInvalid);
    }
}
