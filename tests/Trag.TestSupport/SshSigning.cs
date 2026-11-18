using System.Diagnostics;

namespace Trag.TestSupport;

// A repository that signs with an SSH key made for the test by the ssh-keygen on the PATH, which also signs.
public static class SshSigning
{
    public static async Task UseAsync(Func<string[], Task<string>> git, string container, CancellationToken ct)
    {
        var keygen = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .SelectMany(folder => new[] { Path.Combine(folder, "ssh-keygen.exe"), Path.Combine(folder, "ssh-keygen") }).First(File.Exists);
        var key = Path.Combine(container, "signing-key-" + Guid.NewGuid().ToString("N"));
        using (var made = Process.Start(new ProcessStartInfo(keygen) { ArgumentList = { "-q", "-t", "ed25519", "-N", "", "-f", key }, UseShellExecute = false })!)
            await made.WaitForExitAsync(ct);
        await git(["config", "gpg.format", "ssh"]);
        await git(["config", "gpg.ssh.program", keygen]);
        await git(["config", "user.signingkey", key + ".pub"]);
        await git(["config", "commit.gpgsign", "true"]);
    }
}
