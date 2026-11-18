using System.Security.Cryptography;
using Trag.Domain.Accounts;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Desktop.Diagnostics.Probes;

// Writes, reads back and deletes one uniquely named synthetic secret in the platform store; exit 3 when the store is unavailable.
internal static class VaultProbe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var store = SecretStoreFactory.CreatePlatformStore();
        var status = await store.GetStatusAsync(ct).ConfigureAwait(false);
        if (status != SecretStoreStatus.Available)
        {
            ProbeReport.Write("vault", status.ToString());
            return 3;
        }
        var id = "phase0-" + Guid.NewGuid().ToString("N");
        var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            await store.WriteAsync(id, secret, ct).ConfigureAwait(false);
            var returned = await store.ReadAsync(id, ct).ConfigureAwait(false);
            try
            {
                if (returned is null || !CryptographicOperations.FixedTimeEquals(secret, returned)) throw new InvalidOperationException();
            }
            finally { if (returned is not null) CryptographicOperations.ZeroMemory(returned); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            // Delete only this uniquely named synthetic entry, even on failure.
            await store.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
        if (await store.ReadAsync(id, ct).ConfigureAwait(false) is not null) throw new InvalidOperationException();
        ProbeReport.Write("vault", "Synthetic write/read/delete passed");
        return 0;
    }
}
