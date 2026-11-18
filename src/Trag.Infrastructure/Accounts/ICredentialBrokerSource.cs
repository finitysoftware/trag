using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.Accounts;

// A broker bound to one account, one HTTPS destination and one running operation. The token itself never leaves the
// account layer: the caller gets something that can answer Git, not the credential.
public interface ICredentialBrokerSource
{
    Task<CredentialBroker> CreateBrokerAsync(string accountId, Uri endpoint, CancellationToken ct);
}
