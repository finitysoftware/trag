using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Accounts.Providers;

// What GitHub's OAuth error codes mean for the person signing in. The message is the next thing to do; the code and the
// description GitHub sent go to the details only, redacted the way Git's output is.
internal static class OAuthErrorMessages
{
    public static AccountException Explain(OAuthResponse answer) => new(Message(answer.Error!), Details(answer));

    // A deadline Trag itself saw pass, with no answer from GitHub to show.
    public static AccountException Explain(string error) => new(Message(error));

    // A renewal GitHub refuses leaves one thing to do, whatever the code: sign in again.
    public static AccountException ExplainRenewal(OAuthResponse answer) => new(SignInText.NotRenewable, Details(answer));

    private static string Message(string error) => error switch
    {
        "access_denied" => SignInText.Denied,
        "expired_token" or "token_expired" => SignInText.CodeExpired,
        "device_flow_disabled" or "incorrect_client_credentials" => SignInText.AppRefused,
        _ => SignInText.NotAuthorized
    };

    private static string Details(OAuthResponse answer) => GitOutputRedaction.Redact(SignInText.Answer(answer.Error!, answer.ErrorDescription?.Trim() ?? ""));
}
