namespace Trag.TestSupport;

// A shell script Git runs, as a hook or a program it is configured with: Git outside Windows skips a hook that is not
// executable without a word, and cannot start a program that is not.
public static class ShellScript
{
    public static async Task WriteAsync(string path, string script, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, script, ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
