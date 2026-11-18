using Trag.Application.Ports.Git;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Sync;

// The one object the application asks about a remote and asks to synchronize. It builds its own parts, answers the
// sidebar from local refs only, and before any transfer checks in this order: the request, the repository, the remote
// configuration, the account selection, the upstream target, a clean working tree for a pull and, for a force push, a
// remote tip the branch once contained. Each operation owns what follows.
public sealed class GitSyncFacade : IGitSyncReader, IGitSyncWriter
{
    private readonly IRepositoryReader reader;
    private readonly PresetGitRunner git;
    private readonly RemoteAccountStore? accountStore;
    private readonly GitRefNameValidator refNames;
    private readonly RemoteConfigurationReader configuration;
    private readonly SyncStateReader state;
    private readonly FetchOperation fetch;
    private readonly PushOperation push;
    private readonly PublishOperation publish;
    private readonly RemoteBranchDeleteOperation deletes;
    private readonly RemoteTagOperation tags;
    private readonly LfsTreeInspector lfsFiles;
    private readonly LfsTransfer lfs;
    private readonly GitTransport transport;
    private readonly SubmoduleTransfer submodules;

    public GitSyncFacade(GitProcessRunner runner, IRepositoryReader reader,
        TimeSpan? quietLimit = null,
        IGitAccounts? accounts = null, string? askPassExecutable = null)
    {
        this.reader = reader;
        accountStore = accounts is null ? null : new(runner, accounts);
        refNames = new(runner);
        git = new PresetGitRunner(runner, LocalGitPreset.Writing);
        configuration = new(git, refNames);
        state = new(git, configuration, refNames, accountStore);
        transport = new GitTransport(runner, accountStore, accounts, askPassExecutable, quietLimit ?? SyncTimeouts.Quiet);
        submodules = new(runner, askPassExecutable);
        var local = new PresetGitRunner(runner, LocalGitPreset.Writing);
        lfsFiles = new(git);
        lfs = new LfsTransfer(transport, lfsFiles, new(git));
        fetch = new(transport, state, configuration, new VerifiedMerge(reader, git), lfs);
        var prerequisites = new PushPrerequisites(new(git), lfs);
        push = new(transport, configuration, reader, local, prerequisites);
        publish = new(transport, configuration, reader, git, local, refNames, accounts, accountStore, prerequisites);
        var remoteWrites = new RemoteWriteCheck(reader, configuration, accountStore);
        deletes = new(transport, remoteWrites, local, refNames);
        tags = new(transport, remoteWrites, local);
    }

    public Task<RepositorySyncState> ReadAsync(RepositorySnapshot snapshot, string remote, string? branch, CancellationToken ct) =>
        Task.Run(() => state.ReadStateAsync(snapshot, remote, branch, ct), ct);

    public Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = request switch
            {
                PublishRequest publishRequest => await publish.PublishAsync(publishRequest, ct).ConfigureAwait(false),
                RemoteBranchDeleteRequest deleteRequest => await deletes.DeleteRemoteBranchAsync(deleteRequest, ct).ConfigureAwait(false),
                RemoteTagRequest tagRequest => await tags.RunAsync(tagRequest, ct).ConfigureAwait(false),
                SyncRequest sync => await SynchronizeAsync(sync, ct).ConfigureAwait(false),
                LfsDownloadRequest download => await DownloadLfsAsync(download, ct).ConfigureAwait(false),
                SubmoduleUpdateRequest update => await UpdateSubmodulesAsync(update, ct).ConfigureAwait(false),
                _ => GitWriteResult.Rejected(SyncOutcomeText.TargetMissing)
            };
            return result with { MissingRepository = TransportFailureText.MissesRepository(result.Details) };
        }
        catch (AccountException error) { return GitWriteResult.Rejected(error.Message, error.Details); }
        catch (OperationCanceledException)
        { return new(GitWriteOutcome.Cancelled, ct.IsCancellationRequested ? SyncOutcomeText.Canceled : SyncOutcomeText.TimedOut); }
        catch (Exception error) when (GitFailures.IsExpected(error))
        { return GitWriteResult.Rejected(SyncOutcomeText.NotFinished); }
    });

    // The checks every synchronization passes, in this order: the repository is still the one shown, the remote is
    // configured as it was read and Trag can work with it, the account is the one the user confirmed, the upstream
    // target is a real pair of refs, and a pull is one the pull rule allows. Saving an account stops after the remote.
    private async Task<GitWriteResult> SynchronizeAsync(SyncRequest request, CancellationToken ct)
    {
        var selected = request.Sync;
        request.Progress?.Report(SyncOutcomeText.Checking);
        var fresh = await reader.ReadAsync(request.Expected.Root, ct).ConfigureAwait(false);
        if (!SnapshotComparison.ReadyForSync(request.Expected, fresh))
            return GitWriteResult.Rejected(SyncOutcomeText.StateUnsupported("synchronizing"));
        var config = await configuration.ReadConfigurationAsync(fresh, selected.Remote.Name, ct).ConfigureAwait(false);
        if (config.Remote != selected.Remote) return GitWriteResult.Rejected(SyncOutcomeText.ConfigurationChanged);
        if (config.Remote.Limitation.Length != 0) return GitWriteResult.Rejected(config.Remote.Limitation);
        if (request.Action == SyncAction.SelectAccount) return await SaveAccountAsync(request, fresh, config, ct).ConfigureAwait(false);
        if (accountStore is not null)
        {
            var mapping = await config.ReadAccountAsync(accountStore, fresh.Root, ct).ConfigureAwait(false);
            if (mapping.SelectedId != selected.Accounts?.SelectedId)
                return GitWriteResult.Rejected(SyncOutcomeText.AccountNeedsReview);
            config = config with { AccountId = mapping.SelectedId };
        }
        if (request.Action == SyncAction.Fetch) return await fetch.FetchAsync(request, fresh, config, ct).ConfigureAwait(false);
        if (!await HasUpstreamTargetAsync(fresh, selected.Branch, config, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(SyncOutcomeText.UpstreamMissing);
        if (request.Action == SyncAction.ForcePush && !await WasSeenTipAsync(fresh, selected, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(SyncOutcomeText.RemoteAhead);
        if (request.Action is SyncAction.Push or SyncAction.ForcePush) return await push.PushAsync(request, fresh, config, ct).ConfigureAwait(false);
        // The pull rule the screen asked, on the fresh repository.
        if (selected.PullRefusal(fresh) is { } refused) return GitWriteResult.Rejected(refused);
        return await fetch.PullAsync(request, fresh, config, ct).ConfigureAwait(false);
    }

    // Git LFS content for a merge or a new branch from a remote branch, with the remote's own configuration and the account
    // mapped to it, also when the branch brings the repository's first LFS files. Without Git LFS there is nothing to
    // download with, and Git is not asked; a commit without LFS files needs nothing from the remote, so the remote's
    // configuration, and whether Trag can synchronize with it, does not matter.
    private async Task<GitWriteResult> DownloadLfsAsync(LfsDownloadRequest request, CancellationToken ct)
    {
        var expected = request.Expected;
        if (!expected.LfsInstalled || !await lfsFiles.HasLfsAsync(expected.Root, request.Commit, ct).ConfigureAwait(false))
            return new(GitWriteOutcome.Succeeded, "");
        var config = await configuration.ReadConfigurationAsync(expected, request.Remote, ct).ConfigureAwait(false);
        if (config.Remote.Limitation.Length != 0) return GitWriteResult.Rejected(config.Remote.Limitation);
        if (accountStore is not null)
            config = await config.WithAccountAsync(accountStore, expected.Root, ct).ConfigureAwait(false);
        return await lfs.FetchAsync(expected.Root, config, request.Commit, request.Progress, ct).ConfigureAwait(false) is { } failed
            ? GitWriteResult.Rejected(request.NotDownloaded, GitOutputText.Details(failed))
            : new(GitWriteOutcome.Succeeded, "");
    }

    // Submodules not at the commits the branch records, updated with the account of the remote the branch tracks, or of
    // origin, for the submodules on that remote's host. The outcome is what the repository reads as afterwards.
    private async Task<GitWriteResult> UpdateSubmodulesAsync(SubmoduleUpdateRequest request, CancellationToken ct)
    {
        var fresh = await reader.ReadAsync(request.Expected.Root, ct).ConfigureAwait(false);
        if (!SnapshotComparison.ReadyForSync(request.Expected, fresh)) return GitWriteResult.Rejected(SubmoduleText.Changed);
        var config = (fresh.TrackingRemote ?? fresh.Origin?.Name) is { } remote
            ? await configuration.ReadConfigurationAsync(fresh, remote, ct).ConfigureAwait(false) : null;
        if (config is not null && accountStore is not null) config = await config.WithAccountAsync(accountStore, fresh.Root, ct).ConfigureAwait(false);
        var paths = fresh.SubmodulesToUpdate;
        var details = await submodules.UpdateAsync(fresh.Root, paths,
            token => config is { Remote.Limitation.Length: 0 } ? transport.HostBrokerAsync(fresh.Root, config, token) : Task.FromResult<CredentialBroker?>(null),
            request.Progress, ct).ConfigureAwait(false);
        var after = await reader.ReadAsync(fresh.Root, ct).ConfigureAwait(false);
        return after.SubmodulesToUpdate.Intersect(paths).Any() ? GitWriteResult.Rejected(SubmoduleText.NotUpdated, details)
            : new(GitWriteOutcome.Succeeded, SubmoduleText.Updated, details);
    }

    // A branch may replace only the remote tip it was shown, and only one that was once its own tip.
    private async Task<bool> WasSeenTipAsync(RepositorySnapshot fresh, RepositorySyncState selected, CancellationToken ct) =>
        fresh.Branch is { } branch && selected.TrackingHead is { } seen
        && await BranchHistory.WasTipAsync(git, fresh.Root, "refs/heads/" + branch, seen, ct).ConfigureAwait(false);

    private async Task<GitWriteResult> SaveAccountAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, CancellationToken ct)
    {
        if (accountStore is null) return GitWriteResult.Rejected(SyncOutcomeText.AccountSelectionUnavailable);
        await config.SaveAccountAsync(accountStore, fresh.Root, request.AccountId ?? "", ct).ConfigureAwait(false);
        return new(GitWriteOutcome.Succeeded, SyncOutcomeText.AccountSelectionSaved);
    }

    // A commit to send or receive, a chosen branch, a mapping that really fetches it into the displayed tracking ref,
    // and two ref names Git accepts. Nothing is inferred from the branch name alone.
    private async Task<bool> HasUpstreamTargetAsync(RepositorySnapshot fresh, SyncBranch? branch, RemoteConfiguration config, CancellationToken ct)
    {
        if (fresh.Head is null || branch is null) return false;
        if (!config.Mappings.Any(mapping => mapping.DestinationFor(branch.SourceRef) == branch.TrackingRef)) return false;
        return await refNames.IsValidRefAsync(fresh.Root, branch.SourceRef, false, ct).ConfigureAwait(false)
            && await refNames.IsValidRefAsync(fresh.Root, branch.TrackingRef, false, ct).ConfigureAwait(false);
    }
}
