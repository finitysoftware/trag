using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Providers;

// The code GitHub handed out for one sign-in: what the person types and where, until when it is good, and how often
// GitHub may be asked whether they approved it. GitHub can ask for a longer interval while the code is still valid.
internal sealed class DeviceCode
{
    private DeviceCode(string value, string userCode, Uri verificationUri, DateTimeOffset deadline, int interval)
    {
        Value = value;
        UserCode = userCode;
        VerificationUri = verificationUri;
        Deadline = deadline;
        Interval = interval;
    }

    public string Value { get; }
    public string UserCode { get; }
    public Uri VerificationUri { get; }
    public DateTimeOffset Deadline { get; }
    // Seconds between polls; never under the five GitHub documents.
    public int Interval { get; private set; }

    // Only a response Trag can show and act on becomes a code: a code to type that is short and plain, GitHub's own
    // verification page, a life of at most an hour and an interval of at most five minutes.
    public static DeviceCode Parse(OAuthResponse response, DateTimeOffset now)
    {
        if (response.Error is not null) throw OAuthErrorMessages.Explain(response);
        var usable = !string.IsNullOrWhiteSpace(response.DeviceCode) && !string.IsNullOrWhiteSpace(response.UserCode)
            && response.UserCode.Length <= 32 && response.UserCode.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && response.VerificationUri == AccountMapping.GitHubDevicePage
            && response.ExpiresIn is > 0 and <= 3600 && response.Interval is >= 0 and <= 300;
        if (!usable) throw new AccountException(SignInText.InvalidResponse);
        return new(response.DeviceCode!, response.UserCode!, new(response.VerificationUri!), now.AddSeconds(response.ExpiresIn), Math.Max(5, response.Interval));
    }

    public bool Expired(DateTimeOffset now) => now >= Deadline;

    // The wait before the next poll: the interval, or whatever is left of the code's life, whichever is shorter.
    public TimeSpan DelayUntilNextPoll(DateTimeOffset now)
    {
        var remaining = Deadline - now;
        var interval = TimeSpan.FromSeconds(Interval);
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        return interval < remaining ? interval : remaining;
    }

    // GitHub asked for a longer wait: at least five seconds more than before, or the interval it named.
    public void SlowDown(int requested) => Interval = Math.Max(Interval + 5, Math.Clamp(requested, 0, 3600));
}
