using System.Diagnostics;

namespace Trag.Desktop.Platform;

// Hands paths and addresses to the OS shell through what starts a process, and answers whether it started; the caller
// decides what to say.
public sealed class ShellLauncher(Func<ProcessStartInfo, Process?> start)
{
    public bool Reveal(string path) => TryStart(OperatingSystem.IsWindows()
        ? new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false }
        : new ProcessStartInfo(path) { UseShellExecute = true });

    // A file opened by the program the machine has for its kind; the caller has made sure it is not one that runs.
    public bool Open(string path) => TryStart(new ProcessStartInfo(path) { UseShellExecute = true });

    // The file shown, selected, in the file manager; elsewhere its folder opens.
    public bool RevealFile(string path) => TryStart(OperatingSystem.IsWindows()
        ? new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false }
        : new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });

    // A terminal starting in the folder: Windows Terminal when it is installed, PowerShell otherwise, and the desktop's
    // default terminal elsewhere. The folder goes as an argument or the working directory, never into a command line.
    public bool OpenTerminal(string path)
    {
        var windowsTerminal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return TryStart(!OperatingSystem.IsWindows() ? new ProcessStartInfo("x-terminal-emulator") { WorkingDirectory = path, UseShellExecute = false }
            : File.Exists(windowsTerminal) ? new ProcessStartInfo(windowsTerminal) { ArgumentList = { "-d", path }, UseShellExecute = false }
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoExit" }, WorkingDirectory = path, UseShellExecute = false });
    }

    // Only HTTPS addresses reach the shell; callers validate the host before they get here.
    public bool OpenBrowser(Uri uri) => uri.Scheme == "https" && TryStart(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    // Whether the OS started it; a program, a file manager or a terminal that is not there leaves the repository as it was.
    private bool TryStart(ProcessStartInfo info)
    {
        try { using var started = start(info); return true; }
        catch (Exception) { return false; }
    }
}
