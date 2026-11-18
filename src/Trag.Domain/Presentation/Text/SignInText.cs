namespace Trag.Domain.Presentation.Text;

// What GitHub's device sign-in and its token renewal say when GitHub's answer cannot be used.
public static class SignInText
{
    public const string InvalidResponse = "GitHub returned an invalid sign-in response. Try again.";
    public const string UnreadableResponse = "GitHub returned an unreadable sign-in response.";
    public const string EmptyResponse = "GitHub returned an empty sign-in response.";
    public const string UnsupportedTokenType = "GitHub returned an unsupported token type. Sign in again.";
    public const string NoRepositoryAccess = "GitHub did not grant repository access. Sign in again and approve the repo permission.";

    // GitHub's OAuth errors, by what the user does next.
    public const string Denied = "Sign-in was denied. Choose Add account to try again.";
    public const string CodeExpired = "The sign-in code expired. Choose Add account to get a new code.";
    // Trag's own registered application was refused: nothing the person can change in Trag would help.
    public const string AppRefused = "GitHub does not accept Trag's sign-in right now. Try again later, or install a newer Trag.";
    public const string NotRenewable = "This GitHub session cannot be renewed. Choose Add account in Settings to sign in again.";
    public const string NotAuthorized = "GitHub could not authorize this session. Start sign-in again.";

    // What GitHub answered, for the details: its error code and the description it sent with it.
    public static string Answer(string code, string description) => "GitHub answered " + code + (description.Length == 0 ? "." : ": " + description);
}
