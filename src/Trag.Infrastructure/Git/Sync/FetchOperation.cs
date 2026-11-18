using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Sync;

// Fetching the configured branch mappings, and the pull that continues from a fetch: a pull that is already up to date
// never starts a merge. Git's own tag following brings the tags that point into the fetched history and are not here
// yet; a tag of a name used here is never overwritten. Only a remote set to fetch all its tags (tagOpt --tags) asks for
// every tag it has, and one that differs from the tag of its name here fails the whole atomic fetch.
internal sealed class FetchOperation(GitTransport transport, SyncStateReader state, RemoteConfigurationReader configuration, VerifiedMerge merge,
    LfsTransfer lfs)
{
    public Task<GitWriteResult> FetchAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, CancellationToken ct) =>
        FetchAsync(request, fresh, config, [], ct);

    // Besides the mappings, the refs that have to be on the remote for the fetch to count: Git fails the whole fetch,
    // atomically, when one of them is not there.
    private async Task<GitWriteResult> FetchAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, string[] required, CancellationToken ct)
    {
        request.Progress?.Report(SyncOutcomeText.Fetching);
        var fetch = await transport.TransportAsync(fresh.Root, config, "fetch",
            ["--atomic", "--no-write-fetch-head", "--no-auto-gc", "--progress", "--refmap="],
            [.. config.Mappings.Select(mapping => "+" + mapping.Source + ":" + mapping.Destination), .. required], ct, request.Progress,
            left: new(StoppedWriteLock.RefStorage(fresh.CommonDirectory!, [.. config.Mappings.Select(mapping => mapping.Destination), "refs/tags/*"]))).ConfigureAwait(false);
        if (fetch.ExitCode != 0)
            return GitWriteResult.Rejected(TransportFailureText.Explain(fetch), GitOutputText.Details(fetch)) with { RefusedCredentials = TransportFailureText.RefusesCredentials(fetch) };
        return new(GitWriteOutcome.Succeeded, SyncOutcomeText.Fetched);
    }

    // A pull is a fetch, a fresh look at where the chosen upstream now stands, and a merge only when there is
    // something to merge and the upstream was not rewritten under the branch. The facade has already confirmed the
    // branch and a clean working tree, so after the fetch only the upstream's tip, how far the branch is behind it and
    // whether the branch holds commits the upstream dropped are read, and the remote must still be configured as the
    // pull read it; the branch, index and working tree the merge itself reads again under the index lock. Git LFS files
    // in the incoming commit are downloaded here, as a transfer, so the merge only checks them out. The fetch asks for the
    // upstream itself, as Git's own pull does: one deleted on the remote fails it, where the mappings alone would leave its
    // stale tracking branch to merge unless the user's configuration prunes.
    public async Task<GitWriteResult> PullAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, CancellationToken ct)
    {
        var upstream = request.Sync.Branch!.SourceRef;
        var fetched = await FetchAsync(request, fresh, config, [upstream], ct).ConfigureAwait(false);
        if (UpstreamDeleted(fetched.Details, upstream))
            return GitWriteResult.Rejected(SyncOutcomeText.UpstreamDeleted, fetched.Details);
        if (fetched.Outcome != GitWriteOutcome.Succeeded) return fetched;
        var (tip, behind, rewritten) = await state.ReadUpstreamAsync(fresh, request.Sync.Branch!, ct).ConfigureAwait(false);
        if (tip is null) return GitWriteResult.Rejected(SyncOutcomeText.FetchedUpstreamGone);
        if (behind == 0) return new(GitWriteOutcome.Succeeded, SyncOutcomeText.FetchedNothingToMerge);
        if (rewritten) return GitWriteResult.Rejected(SyncOutcomeText.FetchedUpstreamRewritten);
        if (fresh.LfsInstalled && await lfs.DownloadAsync(fresh.Root, config, tip, request.Progress, ct).ConfigureAwait(false) is { } failed)
            return GitWriteResult.Rejected(SyncOutcomeText.LfsNotFetched, GitOutputText.Details(failed));
        request.Progress?.Report(SyncOutcomeText.MergingFetched);
        if ((await configuration.ReadConfigurationAsync(fresh, config.Remote.Name, ct).ConfigureAwait(false)).Remote != config.Remote)
            return GitWriteResult.Rejected(MergeKind.Pull.Text.Changed);
        return await merge.MergeAsync(new(fresh, tip, MergeKind.Pull, MergeMessages.Pull(request.Sync.Branch.TrackingRef, fresh.Branch!))
            { Progress = request.Progress }, ct).ConfigureAwait(false);
    }

    // Git's own line for a ref the fetch asked for and the remote does not have.
    internal static bool UpstreamDeleted(string? details, string upstream) =>
        details?.Contains("couldn't find remote ref " + upstream, StringComparison.Ordinal) == true;
}
