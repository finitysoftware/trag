using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Sync;

namespace Trag.Infrastructure.Git.Sync;

// Sending the displayed commit to the displayed destination. The destination is read before and after, because the
// server may have accepted the commit just before the connection was lost; only a verified arrival is a success. A force
// push replaces the destination only while it is the tip the user was shown, and Git's lease holds it to that tip. The
// content of Git LFS files goes first, as its own transfer, so the commits never arrive without it.
internal sealed class PushOperation(GitTransport transport, RemoteConfigurationReader configuration,
    IRepositoryReader reader, PresetGitRunner local, PushPrerequisites prerequisites)
{
    public async Task<GitWriteResult> PushAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, CancellationToken ct)
    {
        var branch = request.Sync.Branch!;
        request.Progress?.Report(SyncOutcomeText.CheckingDestination);
        string? before;
        try { before = await transport.RemoteTipAsync(fresh.Root, config, branch.SourceRef, ct).ConfigureAwait(false); }
        catch (RemoteReadException error) { return GitWriteResult.Rejected(SyncOutcomeText.DestinationUnreadable(TransportFailureText.Explain(error.Result)), GitOutputText.Details(error.Result)); }
        if (before == fresh.Head)
        {
            // Another tool sent it; it is recorded as an arrival, so the branch shows up to date without a fetch.
            return await ArrivedAsync(request, fresh, config, SyncOutcomeText.AlreadyThere, ct).ConfigureAwait(false);
        }
        var force = request.Action == SyncAction.ForcePush;
        if (force && (before is null || before != request.Sync.TrackingHead))
            return GitWriteResult.Rejected(SyncOutcomeText.RemoteMoved);
        if (!SnapshotComparison.SameStateForSync(fresh, await reader.ReadAsync(fresh.Root, ct).ConfigureAwait(false))
            || (await configuration.ReadConfigurationAsync(fresh, config.Remote.Name, ct).ConfigureAwait(false)).Remote != config.Remote)
            return GitWriteResult.Rejected(SyncOutcomeText.TargetChanged);
        GitObjectId.Validate(fresh.Head!);
        var check = new RemoteTipCheck(before, fresh.Head);
        if (force) GitObjectId.Validate(before!);
        var lease = force ? "--force-with-lease=" + branch.SourceRef + ":" + before : "--no-force";
        if (await prerequisites.MeetAsync(fresh.Root, config, fresh.Head!, request.Progress, ct).ConfigureAwait(false) is { } missing)
            return missing;
        request.Progress?.Report(SyncOutcomeText.Pushing(force));
        return await VerifiedGitOperation.RunAsync(
            token => transport.TransportAsync(fresh.Root, config, "push",
                ["--porcelain", "--progress", "--no-follow-tags", "--recurse-submodules=no", lease, "--no-force-if-includes"],
                [fresh.Head + ":" + branch.SourceRef], token, request.Progress, left: new(StoppedWriteLock.RefStorage(fresh.CommonDirectory!, branch.TrackingRef))),
            (command, failure, token) => VerifyAsync(request, fresh, config, check, command, failure, ct, token),
            (command, _) => new(GitWriteOutcome.Uncertain, SyncOutcomeText.PushNotVerified, GitOutputText.Details(command)),
            null, SyncTimeouts.Verification, ct).ConfigureAwait(false);
    }

    // The remote is asked again on the verification token; the caller's token only says whether the user gave up.
    private async Task<GitWriteResult> VerifyAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, RemoteTipCheck check,
        GitResult? command, Exception? failure, CancellationToken caller, CancellationToken verification)
    {
        // An account that could not answer stopped the push before Git ever started, so nothing was sent to verify.
        if (failure is AccountException account) return GitWriteResult.Rejected(SyncOutcomeText.PushNotStarted(account.Message), account.Details);
        request.Progress?.Report(SyncOutcomeText.VerifyingDestination);
        if ((await configuration.ReadConfigurationAsync(fresh, config.Remote.Name, verification).ConfigureAwait(false)).Remote != config.Remote)
            return new(GitWriteOutcome.Uncertain, SyncOutcomeText.PushConfigurationChanged);
        var after = await transport.RemoteTipAsync(fresh.Root, config, request.Sync.Branch!.SourceRef, verification).ConfigureAwait(false);
        switch (check.Judge(after, command?.ExitCode, failure is not null))
        {
            case RemoteTipVerdict.Arrived:
                return await ArrivedAsync(request, fresh, config, request.Action == SyncAction.ForcePush ? SyncOutcomeText.Replaced
                    : failure is not null || caller.IsCancellationRequested ? SyncOutcomeText.PushedAfterInterruption : SyncOutcomeText.Pushed,
                    verification).ConfigureAwait(false);
            case RemoteTipVerdict.Interrupted:
                return new(GitWriteOutcome.Cancelled, SyncOutcomeText.PushStopped, GitOutputText.Details(command));
            case RemoteTipVerdict.Refused:
                return GitWriteResult.Rejected(TransportFailureText.Explain(command!), GitOutputText.Details(command));
            default:
                return new(GitWriteOutcome.Uncertain, SyncOutcomeText.DestinationChanged, GitOutputText.Details(command));
        }
    }

    // Only the selected remote-tracking ref moves, never a local branch, and only from the value it was read at. It stays
    // with the fetch address when the push went elsewhere, and the message says where ahead and behind still compare.
    private async Task<GitWriteResult> ArrivedAsync(SyncRequest request, RepositorySnapshot fresh, RemoteConfiguration config, string pushed, CancellationToken ct)
    {
        await RemoteTracking.RecordArrivalAsync(local, fresh.Root, config, request.Sync.Branch!.TrackingRef, fresh.Head!,
            request.Sync.TrackingHead ?? new string('0', fresh.Head!.Length), ct).ConfigureAwait(false);
        return new(GitWriteOutcome.Succeeded,
            config.EffectiveFetch == config.EffectivePush ? pushed : SyncOutcomeText.ComparedWithFetch(pushed, config.Remote.FetchAddress));
    }
}
