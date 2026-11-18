using System.Runtime.InteropServices;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Platform.Secrets;

// libsecret synchronous APIs run only on worker threads and receive GCancellable.
public sealed class LinuxSecretStore : ISecretStore
{
    public Task<SecretStoreStatus> GetStatusAsync(CancellationToken ct) => RunAsync(KeyringStatusReader.CheckStatus, ct);

    public Task<byte[]?> ReadAsync(string accountId, CancellationToken ct) => RunAsync<byte[]?>(cancel =>
    {
        KeyringStatusReader.RequireAvailable(cancel);
        using var attributes = new SecretAttributes(accountId);
        var pointer = LibSecretApi.Lookup(0, attributes.Handle, cancel, out var error);
        KeyringStatusReader.CheckError(error);
        if (pointer == 0) return null;
        // An item another program wrote under the same attributes is no credential of Trag's: there, but unreadable.
        try { return Convert.FromBase64String(Marshal.PtrToStringUTF8(pointer)!); }
        catch (FormatException) { return []; }
        finally { LibSecretApi.PasswordFree(pointer); }
    }, ct);

    public Task WriteAsync(string accountId, ReadOnlyMemory<byte> secret, CancellationToken ct) => RunAsync(cancel =>
    {
        KeyringStatusReader.RequireAvailable(cancel);
        using var attributes = new SecretAttributes(accountId);
        var success = LibSecretApi.Store(0, attributes.Handle, "default", AccountText.SecretLabel, Convert.ToBase64String(secret.Span), cancel, out var error);
        KeyringStatusReader.CheckError(error);
        if (success == 0) throw new SecretStoreException(SecretStoreStatus.Unavailable);
        return true;
    }, ct);

    public Task DeleteAsync(string accountId, CancellationToken ct) => RunAsync(cancel =>
    {
        KeyringStatusReader.RequireAvailable(cancel);
        using var attributes = new SecretAttributes(accountId);
        _ = LibSecretApi.Clear(0, attributes.Handle, cancel, out var error);
        KeyringStatusReader.CheckError(error);
        return true;
    }, ct);

    private static async Task<T> RunAsync<T>(Func<nint, T> operation, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var cancel = GLibInterop.CancellableNew();
                try
                {
                    using var registration = ct.Register(() => GLibInterop.CancellableCancel(cancel));
                    var result = operation(cancel);
                    ct.ThrowIfCancellationRequested();
                    return result;
                }
                finally { GLibInterop.ObjectUnref(cancel); }
            }, ct).ConfigureAwait(false);
        }
        catch (DllNotFoundException)
        {
            if (typeof(T) == typeof(SecretStoreStatus)) return (T)(object)SecretStoreStatus.Unavailable;
            throw new SecretStoreException(SecretStoreStatus.Unavailable);
        }
        catch (SecretStoreException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
    }
}
