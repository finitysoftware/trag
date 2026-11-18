using System.Globalization;
using System.Runtime.InteropServices;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Platform.Storage;

// What Trag leaves behind when an error nothing caught stops it, or when a write caught a defect and Trag went on: one
// file per error in the folder, with the version, the time, the system and the error with its stack, its addresses and
// secrets hidden as in the Details pane. The same error reported twice (by the dispatcher, then by the runtime as Trag
// stops) is written once, and a log that cannot be written leaves the error as it was.
public sealed class CrashLog(string folder, string version, TimeProvider clock)
{
    private Exception? written;

    public void Record(Exception error)
    {
        if (ReferenceEquals(error, written)) return;
        written = error;
        var now = clock.GetUtcNow();
        var text = "Trag " + version + "\n" + now.ToString("O", CultureInfo.InvariantCulture) + "\n"
            + RuntimeInformation.OSDescription + " · " + RuntimeInformation.ProcessArchitecture + "\n\n" + GitOutputRedaction.Hide(error.ToString()) + "\n";
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "crash-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".log"), text);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
}
