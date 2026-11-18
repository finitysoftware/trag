using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Trag.Infrastructure.Platform.Secrets;

// The Windows credential manager as the OS declares it. Nothing here decides anything; it is the P/Invoke surface and
// the one structure those four functions exchange.
[SupportedOSPlatform("windows")]
internal static partial class WindowsCredentialApi
{
    // CRED_TYPE_GENERIC, CRED_PERSIST_LOCAL_MACHINE, and the error the OS returns for a credential that is not there.
    public const uint Generic = 1;
    public const uint LocalMachine = 2;
    public const int NotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    public struct Credential
    {
        public uint Flags, Type;
        public nint TargetName, Comment;
        public uint LastWrittenLow, LastWrittenHigh, BlobSize;
        public nint Blob;
        public uint Persist, AttributeCount;
        public nint Attributes, TargetAlias, UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial int CredRead(string target, uint type, uint flags, out nint credential);
    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    public static partial int CredWrite(in Credential credential, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial int CredDelete(string target, uint type, uint flags);
    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    public static partial void CredFree(nint pointer);
}
