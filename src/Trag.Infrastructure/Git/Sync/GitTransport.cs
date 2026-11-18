using System.Text;
using Trag.Domain;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Sync;

// The one place Git talks to a remote, for as long as the remote keeps answering. A mapped account is re-checked immediately before the transfer and its credentials
// reach Git only through an isolated broker, whose output is redacted and whose buffers are zeroed. Whenever the endpoint
// is not the remote's own configured URL, the transfer runs against a process-only alias whose URL is verified first, so
// a chained rewrite cannot send the data somewhere else. A transfer that updates local refs brings the StoppedWriteLock
// of those refs, so a Git that Cancel or the quiet limit stops leaves none of their locks behind.
internal sealed class GitTransport(GitProcessRunner runner, RemoteAccountStore? accountStore, ICredentialBrokerSource? accounts, string? askPassExecutable,
    TimeSpan quietLimit)
{
    public async Task<GitResult> TransportAsync(string root, RemoteConfiguration config, string command, string[] options, string[] refs,
        CancellationToken ct, IProgress<string>? progress = null, bool pushEndpoint = false, StoppedWriteLock? left = null)
    {
        var endpoint = pushEndpoint || command == "push" ? config.EffectivePush : config.EffectiveFetch;
        await using var broker = await BrokerAsync(root, config, endpoint, ct).ConfigureAwait(false);
        var env = TransportEnvironment.Create(config);
        var setup = TransportEnvironment.Setup(config.Remote.Name);
        if (broker is not null) setup = TransportEnvironment.Authenticated(env, setup, broker, askPassExecutable!);
        var remote = config.Remote.Name;
        if (broker is not null || (pushEndpoint || command == "push") && config.EffectiveFetch != config.EffectivePush)
            (remote, setup) = await VerifiedAliasAsync(root, config, endpoint, setup, env, ct).ConfigureAwait(false);
        // Suppress raw remote progress when authenticated; the UI retains its operation progress.
        GitResult result;
        try
        {
            result = await runner.RunAsync(root, [.. setup, command, .. options, "--", remote, .. refs], ct, env,
                standardErrorProgress: broker is null ? progress : null, literalPathspecs: false, quietLimit: quietLimit).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            left?.Release();
            throw;
        }
        if (broker is not null) return Redacted(result, broker);
        // A password the address carries may be as short as any word Git prints, so it is hidden only from the details.
        return CredentialRedactor.ForAddress(endpoint) is { } address ? result with { HideInDetails = address.Redact } : result;
    }

    // What the remote itself says a branch points at right now, read from its push endpoint, which may differ from the
    // fetch endpoint. A branch the remote does not have is null; anything ambiguous is a failure, never a guess.
    public async Task<string?> RemoteTipAsync(string root, RemoteConfiguration config, string branch, CancellationToken ct)
    {
        var result = await TransportAsync(root, config, "ls-remote", ["--exit-code", "--refs"], [branch], ct, pushEndpoint: true).ConfigureAwait(false);
        if (result.ExitCode == 2) return null;
        if (result.ExitCode != 0) throw new RemoteReadException(result);
        var rows = GitBytes.StrictText(result.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var exact = rows.Select(row => row.Split('\t')).Where(parts => parts.Length == 2 && parts[1].TrimEnd('\r') == branch).ToArray();
        if (exact.Length != 1) throw new FormatException("The remote branch could not be identified.");
        GitObjectId.Validate(exact[0][0]); return exact[0][0];
    }

    // Credentials for the whole host of the remote's fetch address, for the repositories that live beside it, such as its
    // submodules; nothing for Git's own credentials or SSH.
    public Task<CredentialBroker?> HostBrokerAsync(string root, RemoteConfiguration config, CancellationToken ct) =>
        RemoteAddress.Https(config.EffectiveFetch) is { } https ? BrokerAsync(root, config, new Uri(https, "/").AbsoluteUri, ct) : Task.FromResult<CredentialBroker?>(null);

    // Credentials for a mapped account over HTTPS, and nothing for Git's own credentials or SSH. The selection is
    // confirmed once more right here, because the user may have changed it while the operation was already queued.
    private async Task<CredentialBroker?> BrokerAsync(string root, RemoteConfiguration config, string endpoint, CancellationToken ct)
    {
        if (AccountChoice.TragAccount(config.AccountId) is not { } accountId) return null;
        if (accountStore is not null)
        {
            var selection = await config.ReadAccountAsync(accountStore, root, ct).ConfigureAwait(false);
            if (selection.SelectedId != accountId) throw new AccountException(SyncOutcomeText.AccountChangedDuringSync);
        }
        if (RemoteAddress.Https(endpoint) is not { } https) return null;
        TransportEnvironment.RequireAskPass(askPassExecutable);
        return await accounts!.CreateBrokerAsync(accountId, https, ct).ConfigureAwait(false);
    }

    // A process-only remote whose URL is exactly the endpoint, with the original transport options carried over. It has
    // no fetch mappings, so a distinct push URL cannot overwrite the original remote's tracking refs, and an explicit
    // pushurl avoids pushInsteadOf. ls-remote has no --push switch, so the URL is read back before anything is sent.
    private async Task<(string Remote, string[] Setup)> VerifiedAliasAsync(string root, RemoteConfiguration config, string endpoint,
        string[] setup, Dictionary<string, string?> env, CancellationToken ct)
    {
        var alias = "trag-verify-" + Guid.NewGuid().ToString("N");
        env["TRAG_SYNC_VERIFY_URL"] = endpoint;
        string[] verifySetup = [.. setup, "--config-env=remote." + alias + ".url=TRAG_SYNC_VERIFY_URL", "--config-env=remote." + alias + ".pushurl=TRAG_SYNC_VERIFY_URL"];
        for (var index = 0; index < config.TransportOptions.Length; index++)
        {
            var option = config.TransportOptions[index];
            var variable = "TRAG_SYNC_OPTION_" + index;
            env[variable] = option.Value;
            verifySetup = [.. verifySetup, "--config-env=remote." + alias + "." + option.Key + "=" + variable];
        }
        var check = await runner.RunAsync(root, [.. verifySetup, "ls-remote", "--get-url", "--", alias], ct, env).ConfigureAwait(false);
        if (check.ExitCode != 0 || GitBytes.StrictText(check.StandardOutput) != endpoint) throw new ArgumentException("Chained URL rewrites cannot be verified safely.");
        return (alias, verifySetup);
    }

    // What Git printed is redacted by the broker that knows the secret, and the raw buffers are zeroed either way.
    private static GitResult Redacted(GitResult result, CredentialBroker broker)
    {
        try
        {
            return result with
            {
                StandardOutput = Encoding.UTF8.GetBytes(broker.Redact(Encoding.UTF8.GetString(result.StandardOutput))),
                StandardError = Encoding.UTF8.GetBytes(broker.Redact(Encoding.UTF8.GetString(result.StandardError)))
            };
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(result.StandardOutput);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(result.StandardError);
        }
    }
}
