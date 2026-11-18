using System.Text;

namespace Trag.Infrastructure.Accounts.Credentials;

// Whether a prompt that arrived on the pipe may be answered at all: it has to belong to this operation, be one of the
// two prompts Git asks, and name exactly this destination. Git asks again for the same repository only when an answer
// did not work, so four answers are enough for any one of them; a recursive submodule update is one operation in which
// every repository it clones asks for itself.
internal sealed class CredentialAuthorizer(Uri origin, string userName, byte[] secret, string operationId, CancellationToken lifetime)
{
    private readonly Dictionary<string, int> answered = new(StringComparer.Ordinal);

    public byte[]? Authorize(CredentialRequest request)
    {
        if (lifetime.IsCancellationRequested || request.OperationId != operationId) return null;
        var prompt = CredentialPrompt.Parse(request.Prompt);
        if (prompt is null || !prompt.Matches(origin, userName)) return null;
        lock (answered)
        {
            var count = answered.GetValueOrDefault(prompt.Repository) + 1;
            answered[prompt.Repository] = count;
            if (count > 4) return null;
        }
        return prompt.IsPassword ? secret.ToArray() : Encoding.UTF8.GetBytes(userName);
    }
}
