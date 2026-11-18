using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// What Trag leaves behind when an error nothing caught stops it: one file per crash in the crash-log folder, to be read
// or sent on.
public sealed class CrashLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-crash-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime time = new() { Now = new(2026, 10, 12, 9, 30, 15, 250, TimeSpan.Zero) };
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private CrashLog Log() => new(directory, "1.0.0+6a8ecfbd", time);

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (InvalidOperationException error) { return error; }
    }

    [Fact]
    public void WhenAnErrorStopsTrag_ShouldWriteTheVersionTimeSystemAndTheErrorWithItsStack()
    {
        var error = Thrown("The repository vanished.");

        Log().Record(error);

        var file = Assert.Single(Directory.GetFiles(directory));
        Assert.Equal("crash-20261012-093015-250.log", Path.GetFileName(file));
        Assert.Equal("Trag 1.0.0+6a8ecfbd\n2026-10-12T09:30:15.2500000+00:00\n" + RuntimeInformation.OSDescription + " · "
            + RuntimeInformation.ProcessArchitecture + "\n\n" + error + "\n", File.ReadAllText(file));
    }

    // As in the Details pane: an address keeps no credentials and a token is not written.
    [Fact]
    public void WhenTheErrorNamesCredentials_ShouldNotWriteThem()
    {
        Log().Record(Thrown("Could not read https://alice:secret-pass@example.com/repo.git with ghp_abcdef123456"));

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(directory)));
        Assert.DoesNotContain("secret-pass", text); Assert.DoesNotContain("ghp_abcdef123456", text);
        Assert.Contains("https://example.com/repo.git", text);
    }

    // The dispatcher reports an error first; when it lets the error go, the runtime reports it again as Trag stops.
    [Fact]
    public void WhenTheSameErrorIsReportedTwice_ShouldWriteItOnce()
    {
        var log = Log(); var error = Thrown("Once.");

        log.Record(error); time.Now = time.Now.AddSeconds(1); log.Record(error);

        Assert.Single(Directory.GetFiles(directory));
    }

    // A log that cannot be written leaves the error that stops Trag as it was, rather than putting its own in its place.
    [Fact]
    public void WhenTheFolderCannotBeWritten_ShouldLeaveTheErrorAlone()
    {
        File.WriteAllText(directory, "a file where the folder would be");
        try { Log().Record(Thrown("Unwritten.")); }
        finally { File.Delete(directory); }
    }

    // A folder Windows lets nobody create files in refuses the log with UnauthorizedAccessException rather than an IOException.
    [Fact]
    public void WhenTheFolderIsReadOnly_ShouldLeaveTheErrorAlone()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = new DirectoryInfo(directory);
        folder.Create();
        var security = folder.GetAccessControl();
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        security.AddAccessRule(deny);
        folder.SetAccessControl(security);
        try { Log().Record(Thrown("Unwritten.")); }
        finally
        {
            security.RemoveAccessRule(deny);
            folder.SetAccessControl(security);
        }
        Assert.Empty(Directory.GetFiles(directory));
    }
}
