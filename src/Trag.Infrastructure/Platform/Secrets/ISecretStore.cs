using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Platform.Secrets;

// Where an account's credential is kept: the OS store, or memory for an account remembered only until Trag closes.
public interface ISecretStore
{
    Task<SecretStoreStatus> GetStatusAsync(CancellationToken ct);
    Task<byte[]?> ReadAsync(string accountId, CancellationToken ct);
    Task WriteAsync(string accountId, ReadOnlyMemory<byte> secret, CancellationToken ct);
    Task DeleteAsync(string accountId, CancellationToken ct);
}
