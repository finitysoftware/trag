using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Trag.Infrastructure.Git.Processes;

// Every process a started Git starts joins its job, so stopping the job stops them all, also the command a shell of Git
// for Windows forks and then execs, whose Windows parent is gone and which the process tree no longer reaches. Closing the
// job stops nothing: what Git left running after it ended, and what runs when Trag itself ends, keeps running. Git joins
// right after it starts, before it has started anything of its own.
internal sealed partial class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle job = CreateJobObjectW(0, null);

    // A Git that ended before it joined started nothing that stopping the job could reach.
    public WindowsProcessJob(Process process) => AssignProcessToJobObject(job, process.Handle);

    public void Terminate() => TerminateJobObject(job, 1);

    public void Dispose() => job.Dispose();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, nint process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
