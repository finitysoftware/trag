using System.Text.RegularExpressions;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Errors;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Sync;

namespace Trag.Infrastructure.Git.Sync;

// Publish is the one flow that touches the provider, the local configuration, the account mapping and the transport in
// a single run, which is why it takes them all. The run remembers what each step has done, so an interrupted publish
// tells the user exactly where it stopped, and a publish asked again refuses at the remote that now exists instead of
// creating anything twice.
internal sealed class PublishOperation(GitTransport transport, RemoteConfigurationReader configuration,
    IRepositoryReader reader, PresetGitRunner git, PresetGitRunner local, GitRefNameValidator refNames,
    IHostedRepositoryCreator? accounts, RemoteAccountStore? accountStore, PushPrerequisites prerequisites)
{
    // Publish order: check the names and the repository, create the hosted repository, add the remote, record the
    // upstream, read the remote back with its account, then push and verify. The upstream is recorded before the push
    // so a failed push leaves a Push target the user can retry.
    public async Task<GitWriteResult> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        var run = new PublishRun(request);
        try
        {
            request.Progress?.Report(SyncOutcomeText.CheckingRepository);
            if (await RejectNamesAsync(run, ct).ConfigureAwait(false) is { } badName) return badName;
            var fresh = await reader.ReadAsync(run.Root, ct).ConfigureAwait(false);
            if (await RejectStateAsync(run, fresh, ct).ConfigureAwait(false) is { } badState) return badState;
            if (await AddRemoteAsync(run, ct).ConfigureAwait(false) is { } notAdded) return notAdded;
            if (await RecordUpstreamAsync(run, ct).ConfigureAwait(false) is { } notRecorded) return notRecorded;
            var (notConfigured, config) = await ConfigureAsync(run, ct).ConfigureAwait(false);
            if (config is null) return notConfigured!;
            if (!run.Operation.Push) return new(GitWriteOutcome.Succeeded, PublishOutcomeText.NotPushed(run.Changed));
            return await PushAsync(run, fresh, config, ct).ConfigureAwait(false);
        }
        catch (AccountException error) { return GitWriteResult.Rejected(run.Prefix + error.Message, error.Details); }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Cancelled, run.Canceled); }
        catch (Exception error) when (GitFailures.IsExpected(error))
        { return GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.NotFinished); }
    }

    private async Task<GitWriteResult?> RejectNamesAsync(PublishRun run, CancellationToken ct)
    {
        if (!await refNames.IsValidRemoteNameAsync(run.Root, run.Remote, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(PublishOutcomeText.RemoteNameInvalid);
        if (!await refNames.IsValidBranchNameAsync(run.Root, run.Target, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(PublishOutcomeText.TargetNameInvalid);
        return null;
    }

    // The repository has to be the one the user looked at, on the branch being published, and the remote has to be
    // there for an existing one and absent for a new one.
    private async Task<GitWriteResult?> RejectStateAsync(PublishRun run, RepositorySnapshot fresh, CancellationToken ct)
    {
        if (!SnapshotComparison.ReadyForSync(run.Request.Expected, fresh))
            return GitWriteResult.Rejected(SyncOutcomeText.StateUnsupported("publishing"));
        if (run.Branch != fresh.Branch) return GitWriteResult.Rejected(PublishOutcomeText.BranchChanged);
        if (run.Operation.Push && fresh.Head is null) return GitWriteResult.Rejected(PublishOutcomeText.CommitNeeded);
        var exists = await RemoteExistsAsync(run, fresh, ct).ConfigureAwait(false);
        if (run.Kind == PublishKind.ExistingRemote && !exists) return GitWriteResult.Rejected(PublishOutcomeText.RemoteGone);
        if (run.Kind != PublishKind.ExistingRemote && exists)
            return GitWriteResult.Rejected(PublishOutcomeText.RemoteTaken(run.Remote, run.Branch));
        return null;
    }

    // A remote exists when the snapshot lists it or when any of its configuration keys is still there.
    private async Task<bool> RemoteExistsAsync(PublishRun run, RepositorySnapshot fresh, CancellationToken ct)
    {
        if (fresh.Remote(run.Remote) is not null) return true;
        var configured = await git.RunAsync(run.Root, ["config", "--local", "--name-only", "--get-regexp", "^remote\\." + Regex.Escape(run.Remote) + "\\."], ct, allowMissing: true).ConfigureAwait(false);
        return configured.ExitCode == 0;
    }

    // A new repository is created on the provider first; an address typed by the user is checked instead. Either way
    // the remote is then added to the repository. An existing remote is left exactly as it is, and the repository its
    // address names is created first when the publish names one, as for a push that found it missing.
    private async Task<GitWriteResult?> AddRemoteAsync(PublishRun run, CancellationToken ct)
    {
        if (run.Kind == PublishKind.ExistingRemote) return run.Operation.RepositoryName is null ? null : await CreateRepositoryAsync(run, ct).ConfigureAwait(false);
        if (run.Kind == PublishKind.CreateRepository && await CreateRepositoryAsync(run, ct).ConfigureAwait(false) is { } notCreated) return notCreated;
        if (run.Kind == PublishKind.AddRemote && !RemoteUrls.IsValidAddress(run.Address))
            return GitWriteResult.Rejected(PublishOutcomeText.AddressMissing);
        run.Request.Progress?.Report(PublishOutcomeText.Adding(run.Remote));
        var add = await local.ResultAsync(run.Root, ["remote", "add", "--", run.Remote, run.Address!], ct).ConfigureAwait(false);
        if (add.ExitCode != 0)
            return GitWriteResult.Rejected(run.Created is null ? PublishOutcomeText.RemoteNotAdded
                : PublishOutcomeText.CreatedButNotAdded(run.Created, AddressRedactor.Redact(run.Address!), run.Remote), GitOutputText.Details(add));
        run.RemoteAdded = true;
        return null;
    }

    private async Task<GitWriteResult?> CreateRepositoryAsync(PublishRun run, CancellationToken ct)
    {
        var operation = run.Operation;
        if (accounts is null || operation.AccountId is null || operation.RepositoryName is null)
            return GitWriteResult.Rejected(PublishOutcomeText.SignInNeeded);
        run.Request.Progress?.Report(PublishOutcomeText.Creating(operation.RepositoryName.Trim()));
        var repository = await accounts.CreateRepositoryAsync(operation.AccountId, operation.OwnerId, operation.RepositoryName, operation.IsPrivate, ct).ConfigureAwait(false);
        run.Created = repository.FullName;
        run.Address = repository.CloneUrl;
        return null;
    }

    private async Task<GitWriteResult?> RecordUpstreamAsync(PublishRun run, CancellationToken ct)
    {
        run.Request.Progress?.Report(PublishOutcomeText.Recording(run.Upstream));
        // A lock that keeps Git from writing one key keeps it from the other too, so the second answer explains a failure.
        await local.ResultAsync(run.Root, ["config", "--local", "branch." + run.Branch + ".remote", run.Remote], ct).ConfigureAwait(false);
        var setMerge = await local.ResultAsync(run.Root, ["config", "--local", "branch." + run.Branch + ".merge", "refs/heads/" + run.Target], ct).ConfigureAwait(false);
        if (await UpstreamValueAsync(run, "remote", ct).ConfigureAwait(false) != run.Remote
            || await UpstreamValueAsync(run, "merge", ct).ConfigureAwait(false) != "refs/heads/" + run.Target)
            return GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.UpstreamNotRecorded, GitOutputText.Details(setMerge));
        run.UpstreamRecorded = true;
        return null;
    }

    // The upstream counts as recorded only as the configuration now reads, whatever Git's exit said.
    private async Task<string> UpstreamValueAsync(PublishRun run, string key, CancellationToken ct) =>
        GitBytes.Text((await local.ResultAsync(run.Root, ["config", "--local", "--get", "branch." + run.Branch + "." + key], ct).ConfigureAwait(false)).StandardOutput);

    // The remote's configuration is read again now that it exists, and the account chosen for its address is saved and
    // read back as the mapping the transport will check.
    private async Task<(GitWriteResult? Rejected, RemoteConfiguration? Config)> ConfigureAsync(PublishRun run, CancellationToken ct)
    {
        var after = await reader.ReadAsync(run.Root, ct).ConfigureAwait(false);
        RemoteConfiguration config;
        try { config = await configuration.ReadConfigurationAsync(after, run.Remote, ct).ConfigureAwait(false); }
        catch (Exception error) when (error is ArgumentException or FormatException or GitCommandException)
        { return (GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.ConfigurationUnreadable, error.Message), null); }
        if (config.Remote.Limitation.Length != 0) return (GitWriteResult.Rejected(run.Prefix + config.Remote.Limitation), null);
        if (accountStore is null) return (null, config);
        if (run.AccountId is { } accountId)
        {
            try { await config.SaveAccountAsync(accountStore, run.Root, accountId, ct).ConfigureAwait(false); }
            catch (AccountException error) when (error.Message == AccountFailureText.NoMatchingAccount)
            { /* The address does not match the account host; Git's own credentials are used instead. */ }
        }
        return (null, await config.WithAccountAsync(accountStore, run.Root, ct).ConfigureAwait(false));
    }

    // The destination is read first: a branch the remote already has takes the push only as a fast-forward, so a tip
    // that stayed where it was after Git refused is a refusal, and any other tip than the pushed commit belongs to
    // someone else. Git LFS content goes before the commits.
    private async Task<GitWriteResult> PushAsync(PublishRun run, RepositorySnapshot fresh, RemoteConfiguration config, CancellationToken ct)
    {
        GitObjectId.Validate(fresh.Head!);
        string? before;
        try { before = await transport.RemoteTipAsync(run.Root, config, "refs/heads/" + run.Target, ct).ConfigureAwait(false); }
        catch (RemoteReadException error)
        { return GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.NotCompleted(TransportFailureText.Explain(error.Result)), GitOutputText.Details(error.Result)); }
        if (await prerequisites.MeetAsync(run.Root, config, fresh.Head!, run.Request.Progress, ct).ConfigureAwait(false) is { } missing)
            return missing with { Message = run.Prefix + missing.Message };
        run.Request.Progress?.Report(PublishOutcomeText.Pushing(run.Branch, run.Upstream));
        var check = new RemoteTipCheck(before, Expected: fresh.Head);
        // The tracking ref is where the remote's fetch mapping puts the branch; a remote that fetches no such branch gets none.
        var tracking = config.Mappings.Select(mapping => mapping.DestinationFor("refs/heads/" + run.Target)).FirstOrDefault(destination => destination is not null);
        return await VerifiedGitOperation.RunAsync(
            token => transport.TransportAsync(run.Root, config, "push",
                ["--porcelain", "--progress", "--no-follow-tags", "--recurse-submodules=no", "--no-force", "--no-force-if-includes"],
                [fresh.Head + ":refs/heads/" + run.Target], token, run.Request.Progress,
                left: tracking is null ? null : new(StoppedWriteLock.RefStorage(fresh.CommonDirectory!, tracking))),
            (command, failure, token) => VerifyAsync(run, fresh, config, check, tracking, command, failure, token),
            (command, error) => error is RemoteReadException read
                ? new(GitWriteOutcome.Uncertain, run.Prefix + PublishOutcomeText.NotVerified(TransportFailureText.Explain(read.Result)), GitOutputText.Details(read.Result))
                : new(GitWriteOutcome.Uncertain, run.Prefix + PublishOutcomeText.NotVerified(null), GitOutputText.Details(command)),
            null, SyncTimeouts.Verification, ct).ConfigureAwait(false);
    }

    private async Task<GitWriteResult> VerifyAsync(PublishRun run, RepositorySnapshot fresh, RemoteConfiguration config, RemoteTipCheck check,
        string? tracking, GitResult? command, Exception? failure, CancellationToken verification)
    {
        // An account that could not answer stopped the push before Git ever started, so there is nothing on the
        // remote to verify; everything done so far still holds.
        if (failure is AccountException account) return GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.NotStarted(account.Message), account.Details);
        run.Request.Progress?.Report(SyncOutcomeText.VerifyingDestination);
        var tip = await transport.RemoteTipAsync(run.Root, config, "refs/heads/" + run.Target, verification).ConfigureAwait(false);
        switch (check.Judge(tip, command?.ExitCode, failure is not null))
        {
            case RemoteTipVerdict.Arrived:
                // A publish never read the tracking ref before, so there is no earlier value to compare with.
                if (tracking is not null)
                    await RemoteTracking.RecordArrivalAsync(local, run.Root, config, tracking, fresh.Head!, null, verification).ConfigureAwait(false);
                return new(GitWriteOutcome.Succeeded, PublishOutcomeText.Published(run.Branch, run.Created ?? run.Upstream, run.Upstream));
            case RemoteTipVerdict.Interrupted:
                return new(GitWriteOutcome.Cancelled, run.Prefix + PublishOutcomeText.Interrupted, GitOutputText.Details(command));
            case RemoteTipVerdict.Refused:
                return GitWriteResult.Rejected(run.Prefix + PublishOutcomeText.NotCompleted(TransportFailureText.Explain(command!)), GitOutputText.Details(command));
            default:
                return new(GitWriteOutcome.Uncertain, run.Prefix + PublishOutcomeText.DifferentTarget(run.Target), GitOutputText.Details(command));
        }
    }
}
