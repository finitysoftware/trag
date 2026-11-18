using System.Runtime.InteropServices;

namespace Trag.Infrastructure.Platform.Secrets;

// The attributes one Trag credential is stored and looked up by: the application and the account. The hash table and
// every string in it are unmanaged, so the handle owns them and frees them together.
internal sealed class SecretAttributes : IDisposable
{
    private readonly List<nint> strings = [];
    public nint Handle { get; }

    public unsafe SecretAttributes(string id)
    {
        SecretId.Validate(id);
        Handle = GLibInterop.HashNew((nint)(delegate* unmanaged[Cdecl]<nint, uint>)&GLibInterop.Hash,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&GLibInterop.Equal);
        Add("application", "FinitySoftware.Trag");
        Add("account", id);
    }

    private void Add(string key, string value)
    {
        var k = Marshal.StringToCoTaskMemUTF8(key);
        var v = Marshal.StringToCoTaskMemUTF8(value);
        strings.Add(k); strings.Add(v);
        GLibInterop.HashInsert(Handle, k, v);
    }

    public void Dispose()
    {
        GLibInterop.HashUnref(Handle);
        foreach (var pointer in strings) Marshal.FreeCoTaskMem(pointer);
    }
}
