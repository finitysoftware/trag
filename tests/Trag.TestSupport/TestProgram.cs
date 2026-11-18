namespace Trag.TestSupport;

// A program a test puts in Git's place: a batch file on Windows, an executable shell script elsewhere. Each is given in
// its own language, since one does not translate into the other.
public static class TestProgram
{
    public static async Task<string> WriteAsync(string folder, string name, string batch, string shell, CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            var file = Path.Combine(folder, name + ".cmd");
            await File.WriteAllTextAsync(file, batch, ct);
            return file;
        }
        var script = Path.Combine(folder, name);
        await ShellScript.WriteAsync(script, "#!/bin/sh\n" + shell, ct);
        return script;
    }

    public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
