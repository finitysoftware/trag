using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Trag.Infrastructure.Platform.Secrets;

// The GLib pieces libsecret needs: the hash table an attribute lookup is passed in, the error and object handles it
// hands back, and the cancellable a running call watches. The two callbacks are the string hash and comparison GLib
// calls back into, so they are written the way GLib expects: unmanaged, cdecl, over NUL-terminated bytes.
internal static partial class GLibInterop
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe uint Hash(nint pointer)
    {
        uint hash = 5381;
        for (var p = (byte*)pointer; *p != 0; p++) hash = unchecked(hash * 33 + *p);
        return hash;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe int Equal(nint left, nint right)
    {
        var a = (byte*)left; var b = (byte*)right;
        while (*a != 0 && *a == *b) { a++; b++; }
        return *a == *b ? 1 : 0;
    }

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_new")]
    public static partial nint HashNew(nint hash, nint equal);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_insert")]
    public static partial int HashInsert(nint table, nint key, nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_unref")]
    public static partial void HashUnref(nint table);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    public static partial void ErrorFree(nint error);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    public static partial void ObjectUnref(nint value);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_cancellable_new")]
    public static partial nint CancellableNew();
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_cancellable_cancel")]
    public static partial void CancellableCancel(nint cancel);
}
