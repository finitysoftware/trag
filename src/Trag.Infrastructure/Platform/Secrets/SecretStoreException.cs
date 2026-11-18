using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Platform.Secrets;

public sealed class SecretStoreException(SecretStoreStatus status) : Exception($"Secret store is {status}.")
{
    public SecretStoreStatus Status { get; } = status;
}
