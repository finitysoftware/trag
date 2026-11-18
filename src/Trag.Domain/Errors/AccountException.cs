namespace Trag.Domain.Errors;

// An account refusal whose message is the sentence a screen shows; Details is what the provider answered, for the
// details area under it.
public sealed class AccountException(string message, string details = "") : Exception(message)
{
    public string Details { get; } = details;
}
