using System.Security.Cryptography;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Platform.Secrets;

public sealed class MemorySecretStore : ISecretStore, IDisposable
{
    private readonly Dictionary<string, byte[]> secrets = new(StringComparer.Ordinal);
    private readonly object sync = new();
    public Task<SecretStoreStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SecretStoreStatus.MemoryOnly);
    }
    public Task<byte[]?> ReadAsync(string accountId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync) return Task.FromResult(secrets.TryGetValue(accountId, out var value) ? value.ToArray() : null);
    }
    public Task WriteAsync(string accountId, ReadOnlyMemory<byte> secret, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (secrets.Remove(accountId, out var old)) CryptographicOperations.ZeroMemory(old);
            secrets.Add(accountId, secret.ToArray());
        }
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string accountId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync) if (secrets.Remove(accountId, out var old)) CryptographicOperations.ZeroMemory(old);
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        lock (sync)
        {
            foreach (var value in secrets.Values) CryptographicOperations.ZeroMemory(value);
            secrets.Clear();
        }
    }
}
