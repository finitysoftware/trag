using System.Security.Cryptography;
using System.Text;
using Trag.Desktop.Diagnostics.Fixtures;
using Trag.Domain.Accounts;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Desktop.Diagnostics.Probes;

// Explicit probe mode only. All identities and tokens are synthetic; no HTTP leaves the process: GitLab accounts answer
// the exact repository's credential fill and nothing else, GitHub device login, verification and token refresh pass.
internal static class AccountProbe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        await RunTransportAsync(Environment.ProcessPath!, ct).ConfigureAwait(false);
        ProbeReport.Write("accounts", "Synthetic GitHub device login/refresh, GitLab accounts, exact repository askpass and helper isolation passed");
        return 0;
    }

    private static async Task RunTransportAsync(string executable, CancellationToken ct)
    {
        using var service = SyntheticAccountService.Create();
        var runner = new GitProcessRunner();
        foreach (var name in new[] { "alice", "bob" })
            await CheckGitLabCredentialAsync(service, runner, executable, name, ct);
        await CheckGitHubRefreshAsync(service, ct);
    }

    // The account's token answers Git's credential fill for exactly its repository, past a deliberately wrong helper,
    // and the askpass helper refuses every other destination.
    private static async Task CheckGitLabCredentialAsync(AccountService service, GitProcessRunner runner, string executable, string name, CancellationToken ct)
    {
        var token = "fixture-" + name;
        var account = await service.SignInGitLabAsync("https://example.invalid:8443/team", Encoding.UTF8.GetBytes(token), true, ct);
        await using var broker = await service.CreateBrokerAsync(account.Id, new("https://example.invalid:8443/team/project.git"), ct);
        var result = await runner.RunAsync(Path.GetTempPath(), ["-c", "credential.helper=!echo username=wrong; echo password=wrong",
            "-c", "credential.https://example.invalid:8443/team/project.git.helper=!echo username=wrong; echo password=wrong",
            .. CredentialBroker.IsolatedHelperArguments, .. broker.EndpointArguments, "credential", "fill"], ct,
            broker.CreateEnvironment(executable), "protocol=https\nhost=example.invalid:8443\npath=team/project.git\n\n"u8.ToArray());
        try
        {
            var value = Encoding.UTF8.GetString(result.StandardOutput);
            if (result.ExitCode != 0 || !value.Contains("username=" + name, StringComparison.Ordinal) || !value.Contains("password=" + token, StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic repository credential selection failed.");
        }
        finally { CryptographicOperations.ZeroMemory(result.StandardOutput); CryptographicOperations.ZeroMemory(result.StandardError); }
        foreach (var url in new[] { "https://alice@other.invalid:8443/team/project.git", "https://alice@example.invalid:8443/team/other.git" })
        {
            using var output = new MemoryStream();
            if (await AskPassClient.RunAsync(broker.PipeName, broker.OperationId, "Password for '" + url + "': ", output, ct) == 0)
                throw new InvalidOperationException("Synthetic credential was disclosed outside its destination.");
        }
    }

    // Device login and identity verification pass, and the broker hands out the renewed token after the refresh.
    private static async Task CheckGitHubRefreshAsync(AccountService service, CancellationToken ct)
    {
        var github = await service.SignInGitHubAsync(true, new IgnoreProgress(), ct);
        var verified = await service.VerifyAsync(github.Id, ct);
        if (verified.ProviderUserId != "7") throw new InvalidOperationException("Synthetic GitHub identity or refresh failed.");
        await using var githubBroker = await service.CreateBrokerAsync(github.Id, new("https://github.com/fixture/project.git"), ct);
        var refreshed = githubBroker.Authorize(new(githubBroker.OperationId, "Password for 'https://alice@github.com/fixture/project.git': "));
        try { if (refreshed is null || !refreshed.AsSpan().SequenceEqual("synthetic-renewed"u8)) throw new InvalidOperationException("Synthetic GitHub token was not renewed."); }
        finally { if (refreshed is not null) CryptographicOperations.ZeroMemory(refreshed); }
    }

    private sealed class IgnoreProgress : IProgress<DeviceLoginProgress> { public void Report(DeviceLoginProgress value) { } }
}
