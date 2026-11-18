using Trag.Domain.Accounts;

namespace Trag.Application.Accounts;

// How an account operation ended: the settings read again when it changed them, and the one sentence that says so.
public sealed record AccountOutcome(AccountSettings? Settings, string Status)
{
    public bool Succeeded { get; init; } = true;
    // A sign-in connected an account, which closes the token entry.
    public bool SignedIn { get; init; }
    // The account is gone from the service, even when reading the settings afterwards failed.
    public bool Removed { get; init; }

    public static AccountOutcome Failure(string status) => new(null, status) { Succeeded = false };
}
