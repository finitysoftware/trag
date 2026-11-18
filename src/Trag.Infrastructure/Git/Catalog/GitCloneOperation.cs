using System.ComponentModel;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.Git.Catalog;

// The one clone Trag runs. The target is checked before Git starts; Git clones the repository with the chosen account's
// credentials or its own, leaving Git LFS content for a step of its own; that step and each submodule then run with
// credentials asked for when they start; and a clone that finished still has to be a usable working copy. Everything
// Git refuses comes back as a sentence with the redacted details under it. Git's clone and Git LFS's download stop after the
// quiet limit without a byte from the server, as every transfer does.
internal sealed class GitCloneOperation(GitProcessRunner runner, GitRepositoryProbe probe, ICredentialBrokerSource? accounts, string? askPassExecutable,
    TimeSpan quietLimit)
{
    private readonly SubmoduleTransfer submodules = new(runner, askPassExecutable);
    private readonly LfsTreeInspector lfsFiles = new(new PresetGitRunner(runner, LocalGitPreset.Writing));
    private readonly PresetGitRunner reading = new(runner, LocalGitPreset.Reading);

    public Task<CloneResult> CloneAsync(CloneRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var (target, rejection) = CloneTarget.Resolve(request);
            if (target is null) return new CloneResult(false, rejection!);
            Directory.CreateDirectory(target.Parent);
            var existed = Directory.Exists(target.Folder);
            CloneResult outcome;
            try { outcome = await CloneIntoAsync(request, target, ct).ConfigureAwait(false); }
            catch (Exception error) when (Failure(error, ct) is { } failure) { outcome = failure; }
            catch (Exception)
            {
                ClonedFolder.TakeBack(target.Folder, existed);
                throw;
            }
            // A clone that ends in any failure is not listed, so it leaves nothing behind either; Git leaves a clone behind
            // when only its checkout failed, and the same clone could then not be tried again.
            return outcome.Succeeded || ClonedFolder.TakeBack(target.Folder, existed)
                ? outcome
                : outcome with { Message = outcome.Message + " " + CloneText.Left(target.Folder) };
        }
        catch (Exception error) when (Failure(error, ct) is { } failure) { return failure; }
    }, ct);

    private static CloneResult? Failure(Exception error, CancellationToken ct) => error switch
    {
        OperationCanceledException => new(false, ct.IsCancellationRequested ? CloneText.Canceled : CloneText.TimedOut),
        AccountException account => new(false, account.Message, account.Details),
        Win32Exception { NativeErrorCode: 2 } => new(false, SessionText.Error(RepositoryError.GitMissing)),
        _ when GitFailures.IsExpected(error) => new(false, CloneText.NotFinished),
        _ => null
    };

    private async Task<CloneResult> CloneIntoAsync(CloneRequest request, CloneTarget target, CancellationToken ct)
    {
        request.Progress?.Report(CloneText.Cloning);
        var (result, said, details) = await RunAsync(request, target.Parent,
            ["clone", "--progress", "--no-recurse-submodules", "--", target.Address, target.Folder], skipLfs: true, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return new CloneResult(false, CloneFailureText.Explain(said), details);
        if (await DownloadLfsAsync(request, target, ct).ConfigureAwait(false) is { } lfs) return new CloneResult(false, CloneText.LfsNotDownloaded, lfs);
        // A submodule Git could not clone leaves the clone itself, with Git's words; the submodule is left to Update
        // submodules.
        var submoduleDetails = request.Submodules ? await CloneSubmodulesAsync(request, target, ct).ConfigureAwait(false) : "";
        var probed = await probe.ProbeAsync(target.Folder, ct).ConfigureAwait(false);
        return probed.Status == RepositoryLinkStatus.Valid
            ? new CloneResult(true, CatalogText.Cloned, submoduleDetails)
            : new CloneResult(false, CloneText.NotWorkingCopy, details);
    }

    // Git LFS content of the commit checked out, where Git LFS is installed and the commit has LFS files: Git LFS's
    // details when it could not download it, or null.
    private async Task<string?> DownloadLfsAsync(CloneRequest request, CloneTarget target, CancellationToken ct)
    {
        var filter = await reading.ResultAsync(target.Folder, ["config", "--get", "filter.lfs.process"], ct).ConfigureAwait(false);
        if (filter.ExitCode != 0 || GitBytes.Text(filter.StandardOutput).Length == 0
            || (await reading.ResultAsync(target.Folder, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false)).ExitCode != 0
            || !await lfsFiles.HasLfsAsync(target.Folder, "HEAD", ct).ConfigureAwait(false))
            return null;
        request.Progress?.Report(SyncOutcomeText.FetchingLfs);
        var (result, _, details) = await RunAsync(request, target.Folder, ["lfs", "pull"], skipLfs: false, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? null : details;
    }

    // The submodules the clone names, each with an account broker of its own for the whole host, since submodules usually
    // live beside the repository.
    private async Task<string> CloneSubmodulesAsync(CloneRequest request, CloneTarget target, CancellationToken ct)
    {
        var listed = await SubmodulePaths.ListedAsync(reading, target.Folder, ct).ConfigureAwait(false);
        var paths = await SubmodulePaths.GitlinksAsync(reading, target.Folder, listed.Select(submodule => submodule.Path), ct).ConfigureAwait(false);
        return await submodules.UpdateAsync(target.Folder, paths, token => BrokerAsync(request.AccountId, target.Address, hostWide: true, token), request.Progress, ct)
            .ConfigureAwait(false);
    }

    // One Git run of the clone: no remote configuration and no remote name yet, and a credential broker of its own; the
    // clone of the repository itself leaves Git LFS content for its own step, and that step lets Git LFS give Git its own
    // pathspecs, which it writes with magic. The account's credential is hidden before the shared redaction, because only
    // the broker knows it; a password the address carries may be as short as any word Git prints, so it is hidden only
    // from the details, and the sentence is read from what Git said.
    private async Task<(GitResult Result, string Said, string Details)> RunAsync(CloneRequest request, string directory, string[] command, bool skipLfs, CancellationToken ct)
    {
        var env = TransportEnvironment.Create(null);
        if (skipLfs) env["GIT_LFS_SKIP_SMUDGE"] = "1";
        var setup = TransportEnvironment.Setup(null);
        await using var broker = await BrokerAsync(request.AccountId, request.CloneUrl.Trim(), hostWide: false, ct).ConfigureAwait(false);
        if (broker is not null) setup = TransportEnvironment.Authenticated(env, setup, broker, askPassExecutable!);
        var result = await runner.RunAsync(directory, [.. setup, .. command], ct, env, standardErrorProgress: broker is null ? request.Progress : null,
            literalPathspecs: skipLfs, quietLimit: quietLimit).ConfigureAwait(false);
        var text = GitOutputText.Raw(result);
        var said = GitOutputRedaction.Redact(broker is null ? text : broker.Redact(text)).Trim();
        var details = broker is null && CredentialRedactor.ForAddress(request.CloneUrl.Trim()) is { } address ? GitOutputRedaction.Redact(address.Redact(text)).Trim() : said;
        return (result, said, details);
    }

    // Credentials only for a chosen account over HTTPS; SSH addresses and Git's own credentials need no broker.
    private async Task<CredentialBroker?> BrokerAsync(string? accountId, string address, bool hostWide, CancellationToken ct)
    {
        if (accountId is null || accounts is null || RemoteAddress.Https(address) is not { } https) return null;
        TransportEnvironment.RequireAskPass(askPassExecutable);
        return await accounts.CreateBrokerAsync(accountId, hostWide ? new(https, "/") : https, ct).ConfigureAwait(false);
    }
}
