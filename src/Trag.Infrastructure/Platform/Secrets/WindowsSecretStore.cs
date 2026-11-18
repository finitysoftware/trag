using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Platform.Secrets;

// Trag's credentials in the Windows credential manager, one target per account. Every call runs on a worker thread,
// because the API is synchronous, and a credential that is not there is an answer rather than a failure.
[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStore : ISecretStore
{
    private static string Target(string id)
    {
        SecretId.Validate(id);
        return "FinitySoftware/Trag/" + id;
    }

    public async Task<SecretStoreStatus> GetStatusAsync(CancellationToken ct)
    {
        try { await ReadAsync("availability-probe", ct).ConfigureAwait(false); return SecretStoreStatus.Available; }
        catch (SecretStoreException error) { return error.Status; }
    }

    public Task<byte[]?> ReadAsync(string accountId, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (WindowsCredentialApi.CredRead(Target(accountId), WindowsCredentialApi.Generic, 0, out var pointer) == 0)
        {
            if (Marshal.GetLastPInvokeError() == WindowsCredentialApi.NotFound) return null;
            throw new SecretStoreException(SecretStoreStatus.Unavailable);
        }
        try
        {
            unsafe
            {
                var credential = (WindowsCredentialApi.Credential*)pointer;
                return new ReadOnlySpan<byte>((void*)credential->Blob, checked((int)credential->BlobSize)).ToArray();
            }
        }
        finally { WindowsCredentialApi.CredFree(pointer); }
    }, ct);

    public Task WriteAsync(string accountId, ReadOnlyMemory<byte> secret, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (secret.Length > 2560) throw new ArgumentOutOfRangeException(nameof(secret));
        unsafe
        {
            fixed (char* target = Target(accountId))
            fixed (byte* blob = secret.Span)
            {
                var credential = new WindowsCredentialApi.Credential { Type = WindowsCredentialApi.Generic, TargetName = (nint)target,
                    Blob = (nint)blob, BlobSize = (uint)secret.Length, Persist = WindowsCredentialApi.LocalMachine };
                if (WindowsCredentialApi.CredWrite(in credential, 0) == 0) throw new SecretStoreException(SecretStoreStatus.Unavailable);
            }
        }
    }, ct);

    public Task DeleteAsync(string accountId, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (WindowsCredentialApi.CredDelete(Target(accountId), WindowsCredentialApi.Generic, 0) == 0
            && Marshal.GetLastPInvokeError() != WindowsCredentialApi.NotFound)
            throw new SecretStoreException(SecretStoreStatus.Unavailable);
    }, ct);
}
