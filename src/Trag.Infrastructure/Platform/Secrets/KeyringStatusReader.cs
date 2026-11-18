using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Platform.Secrets;

// Whether the keyring can be used at all: the service has to answer, the default collection has to exist, and it has
// to be unlocked. Every handle taken here is released here, including on the paths that fail.
internal static class KeyringStatusReader
{
    public static SecretStoreStatus CheckStatus(nint cancel)
    {
        var service = LibSecretApi.ServiceGet(0, cancel, out var error);
        if (error != 0) { GLibInterop.ErrorFree(error); return SecretStoreStatus.Unavailable; }
        if (service == 0) return SecretStoreStatus.Unavailable;
        try
        {
            var collection = LibSecretApi.CollectionForAlias(service, "default", 0, cancel, out error);
            if (error != 0) { GLibInterop.ErrorFree(error); return SecretStoreStatus.Unavailable; }
            if (collection == 0) return SecretStoreStatus.Unavailable;
            try { return LibSecretApi.CollectionLocked(collection) != 0 ? SecretStoreStatus.Locked : SecretStoreStatus.Available; }
            finally { GLibInterop.ObjectUnref(collection); }
        }
        finally { GLibInterop.ObjectUnref(service); }
    }

    public static void RequireAvailable(nint cancel)
    {
        var status = CheckStatus(cancel);
        if (status != SecretStoreStatus.Available) throw new SecretStoreException(status);
    }

    public static void CheckError(nint error)
    {
        if (error == 0) return;
        GLibInterop.ErrorFree(error);
        throw new SecretStoreException(SecretStoreStatus.Unavailable);
    }
}
