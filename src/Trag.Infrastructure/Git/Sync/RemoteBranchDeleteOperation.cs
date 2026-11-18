using Trag.Domain;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Sync;

namespace Trag.Infrastructure.Git.Sync;

// Deleting a remote branch is a push of nothing: the remote tip is read first, the deletion is sent only while the branch
// is still there (a push someone made meanwhile stays), and the result is verified with ls-remote. The local
// remote-tracking reference is removed only after the remote confirmed the deletion, and only when the remote fetches
// from where it pushes.
internal sealed class RemoteBranchDeleteOperation(GitTransport transport, RemoteWriteCheck check,
    PresetGitRunner local, GitRefNameValidator refNames)
{
    public async Task<GitWriteResult> DeleteRemoteBranchAsync(RemoteBranchDeleteRequest request, CancellationToken ct)
    {
        var (root, remote, name) = (request.Expected.Root, request.Remote, request.Name.Trim());
        var label = remote + "/" + name;
        try
        {
            request.Progress?.Report(SyncOutcomeText.CheckingRepository);
            var (rejected, config) = await CheckAsync(request, remote, name, ct).ConfigureAwait(false);
            if (config is null) return rejected!;
            // The branch on the remote is the one the fetch mapping brings in as this remote-tracking branch.
            if (config.Mappings.Select(mapping => mapping.SourceFor("refs/remotes/" + label)).FirstOrDefault(source => source is not null) is not { } branch)
                return GitWriteResult.Rejected(RemoteBranchDeleteText.NotFetched(label, remote));
            string? before;
            try { before = await transport.RemoteTipAsync(root, config, branch, ct).ConfigureAwait(false); }
            catch (RemoteReadException error) { return GitWriteResult.Rejected(RemoteBranchDeleteText.RemoteUnreadable(TransportFailureText.Explain(error.Result)), GitOutputText.Details(error.Result)); }
            if (before is null)
            {
                await RemoteTracking.RecordDeletionAsync(local, root, config, "refs/remotes/" + label, ct).ConfigureAwait(false);
                return new(GitWriteOutcome.Succeeded, RemoteBranchDeleteText.AlreadyGone(label, remote));
            }
            var check = new RemoteTipCheck(before, Expected: null);
            request.Progress?.Report(RemoteBranchDeleteText.Deleting(branch["refs/heads/".Length..], remote));
            // Git deletes the tracking ref itself once the remote took the deletion, packed-refs included.
            return await VerifiedGitOperation.RunAsync(
                token => transport.TransportAsync(root, config, "push", ["--porcelain", "--progress", "--no-follow-tags", "--force-with-lease=" + branch + ":" + before, "--delete"],
                    [branch], token, request.Progress, left: new(StoppedWriteLock.RefStorage(request.Expected.CommonDirectory!, "refs/remotes/" + label, "packed-refs"))),
                (command, failure, token) => VerifyAsync(request, config, check, label, branch, command, failure, token),
                (command, error) => error is RemoteReadException read
                    ? new(GitWriteOutcome.Uncertain, RemoteBranchDeleteText.NotVerified(label, TransportFailureText.Explain(read.Result)), GitOutputText.Details(read.Result))
                    : new(GitWriteOutcome.Uncertain, RemoteBranchDeleteText.NotVerified(label, null), GitOutputText.Details(command)),
                null, SyncTimeouts.Verification, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Cancelled, RemoteBranchDeleteText.Canceled(label)); }
        catch (Exception error) when (GitFailures.IsExpected(error))
        { return GitWriteResult.Rejected(RemoteBranchDeleteText.Failed); }
    }

    // The name Git accepts, then what every write to a remote checks: everything that has to hold before the remote is
    // contacted at all.
    private async Task<(GitWriteResult? Rejected, RemoteConfiguration? Config)> CheckAsync(RemoteBranchDeleteRequest request, string remote, string name, CancellationToken ct)
    {
        if (!await refNames.IsValidBranchNameAsync(request.Expected.Root, name, ct).ConfigureAwait(false))
            return (GitWriteResult.Rejected(RemoteBranchDeleteText.NameInvalid), null);
        return await check.PrepareAsync(request.Expected, remote, "deleting", RemoteBranchDeleteText.RemoteGone, ct).ConfigureAwait(false);
    }

    private async Task<GitWriteResult> VerifyAsync(RemoteBranchDeleteRequest request, RemoteConfiguration config, RemoteTipCheck check, string label, string branch,
        GitResult? command, Exception? failure, CancellationToken verification)
    {
        // An account that could not answer stopped the deletion before Git ever started, so there is nothing on the
        // remote to verify.
        if (failure is AccountException account) return GitWriteResult.Rejected(RemoteBranchDeleteText.NotStarted(account.Message), account.Details);
        request.Progress?.Report(SyncOutcomeText.VerifyingRemote);
        var root = request.Expected.Root;
        var after = await transport.RemoteTipAsync(root, config, branch, verification).ConfigureAwait(false);
        var pointed = check.Before!;
        switch (check.Judge(after, command?.ExitCode, failure is not null))
        {
            case RemoteTipVerdict.Arrived:
                await RemoteTracking.RecordDeletionAsync(local, root, config, "refs/remotes/" + label, verification).ConfigureAwait(false);
                return new(GitWriteOutcome.Succeeded, RemoteBranchDeleteText.Deleted(label, request.Remote, GitObjectId.Short(pointed, 8)));
            case RemoteTipVerdict.Interrupted:
                return new(GitWriteOutcome.Cancelled, RemoteBranchDeleteText.Interrupted(label), GitOutputText.Details(command));
            case RemoteTipVerdict.Refused:
                return GitWriteResult.Rejected(RemoteBranchDeleteText.Refused(label, TransportFailureText.Explain(command!)), GitOutputText.Details(command));
            default:
                return new(GitWriteOutcome.Uncertain, RemoteBranchDeleteText.ChangedWhileDeleting(label), GitOutputText.Details(command));
        }
    }
}
