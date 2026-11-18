using System.Runtime.InteropServices;

namespace Trag.Infrastructure.Platform.Secrets;

// libsecret as the library declares it. Only the synchronous entry points are used, and every one of them takes a
// GCancellable so a call can be stopped from another thread.
internal static partial class LibSecretApi
{
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_service_get_sync")]
    public static partial nint ServiceGet(int flags, nint cancel, out nint error);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_collection_for_alias_sync", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint CollectionForAlias(nint service, string alias, int flags, nint cancel, out nint error);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_collection_get_locked")]
    public static partial int CollectionLocked(nint collection);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_lookupv_sync")]
    public static partial nint Lookup(nint schema, nint attributes, nint cancel, out nint error);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Store(nint schema, nint attributes, string collection, string label, string password, nint cancel, out nint error);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_clearv_sync")]
    public static partial int Clear(nint schema, nint attributes, nint cancel, out nint error);
    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_free")]
    public static partial void PasswordFree(nint password);
}
