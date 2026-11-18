using System.Security.Cryptography;
using System.Text;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Desktop.Diagnostics.Probes;

// Two synthetic accounts answer Git's credential fill through this exe as askpass; a foreign host must be refused.
// Shared by --auth-probe and the --ui-auth-smoke launch.
internal static class AuthProbe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        await RunCredentialsAsync(Environment.ProcessPath!, ct).ConfigureAwait(false);
        ProbeReport.Write("auth", "Synthetic two-account Git askpass passed");
        return 0;
    }

    public static async Task RunCredentialsAsync(string executable, CancellationToken ct)
    {
        var runner = new GitProcessRunner();
        foreach (var name in new[] { "alice", "bob" })
        {
            var token = Encoding.UTF8.GetBytes("synthetic-" + name);
            try
            {
                await using var broker = new CredentialBroker(new("https://example.invalid"), name, token, ct);
                // A deliberately wrong earlier helper must be cleared for this process.
                var result = await runner.RunAsync(Path.GetTempPath(), ["-c", "credential.helper=!echo username=wrong; echo password=wrong",
                    .. CredentialBroker.IsolatedHelperArguments, "credential", "fill"], ct,
                    broker.CreateEnvironment(executable), "protocol=https\nhost=example.invalid\n\n"u8.ToArray()).ConfigureAwait(false);
                var value = Encoding.UTF8.GetString(result.StandardOutput);
                var diagnostic = Encoding.UTF8.GetString(result.StandardError);
                if (result.ExitCode != 0 || !value.Contains("username=" + name, StringComparison.Ordinal)
                    || !value.Contains("password=synthetic-" + name, StringComparison.Ordinal))
                {
                    ProbeReport.Write("synthetic-auth-diagnostic", $"Git exit={result.ExitCode}; cannot-run={diagnostic.Contains("cannot run", StringComparison.Ordinal)}; cannot-read={diagnostic.Contains("could not read", StringComparison.Ordinal)}; terminal-disabled={diagnostic.Contains("terminal prompts disabled", StringComparison.Ordinal)}; output-length={value.Length}");
                    throw new InvalidOperationException("Synthetic Git credential check failed.");
                }
                using var deniedOutput = new MemoryStream();
                if (await AskPassClient.RunAsync(broker.PipeName, broker.OperationId,
                    $"Password for 'https://{name}@other.invalid': ", deniedOutput, ct).ConfigureAwait(false) == 0)
                    throw new InvalidOperationException("Foreign host was accepted.");
            }
            finally { CryptographicOperations.ZeroMemory(token); }
        }
    }
}
