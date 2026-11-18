using Trag.Domain.Accounts;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.TestSupport;

public sealed class FakeSecretStore : ISecretStore
{
    public SecretStoreStatus Status { get; set; } = SecretStoreStatus.Available;
    public Dictionary<string, byte[]> Secrets { get; } = new(StringComparer.Ordinal);
    public Exception? Failure { get; set; }
    // The account ids each call was made for, in order: a test that counts writes usually also cares which account
    // was written, and a test that expects none can assert the list is empty.
    public List<string> Reads { get; } = [];
    public List<string> Writes { get; } = [];
    public int StatusReads { get; private set; }

    public Task<SecretStoreStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        StatusReads++;
        return Task.FromResult(Status);
    }

    public Task<byte[]?> ReadAsync(string accountId, CancellationToken cancellationToken)
    {
        Reads.Add(accountId);
        Refuse();
        return Task.FromResult(Secrets.TryGetValue(accountId, out var secret) ? secret.ToArray() : null);
    }

    public Task WriteAsync(string accountId, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken)
    {
        Writes.Add(accountId);
        Refuse();
        Secrets[accountId] = secret.ToArray();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken)
    {
        Refuse();
        Secrets.Remove(accountId);
        return Task.CompletedTask;
    }

    private void Refuse()
    {
        if (Failure is not null) throw Failure;
        // The OS stores refuse every operation while the vault is not available, and so does this one.
        if (Status != SecretStoreStatus.Available) throw new SecretStoreException(Status);
    }
}
