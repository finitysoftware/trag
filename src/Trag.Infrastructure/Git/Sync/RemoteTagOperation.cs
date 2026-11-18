using System.Text;
using Trag.Domain.Errors;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Sync;

namespace Trag.Infrastructure.Git.Sync;

// A tag on a remote, sent there or deleted there, like a branch: the remote's tag is read first, only the tag is pushed
// (or nothing, to delete it), and the result is verified with ls-remote. A remote that already holds this very tag needs
// no push, one that holds another tag of the name is never overwritten, one without the tag needs no deletion, and a tag
// replaced there meanwhile is not deleted. The
// local tag stays as it is.
internal sealed class RemoteTagOperation(GitTransport transport, RemoteWriteCheck check, PresetGitRunner local)
{
    public async Task<GitWriteResult> RunAsync(RemoteTagRequest request, CancellationToken ct)
    {
        var (root, remote, name) = (request.Expected.Root, request.Remote, request.Name.Trim());
        var tagRef = "refs/tags/" + name;
        var pushing = request.Action == RemoteTagAction.Push;
        try
        {
            if (!TagNames.IsValid(name)) return GitWriteResult.Rejected(RemoteTagText.NameInvalid);
            request.Progress?.Report(SyncOutcomeText.CheckingRepository);
            var (rejected, config) = await check.PrepareAsync(request.Expected, remote, pushing ? "pushing" : "deleting", RemoteTagText.RemoteGone, ct).ConfigureAwait(false);
            if (config is null) return rejected!;
            string? tag = null;
            if (pushing && (tag = await LocalTagAsync(root, tagRef, ct).ConfigureAwait(false)) is null) return GitWriteResult.Rejected(RemoteTagText.Missing(name));
            string? before;
            try { before = await transport.RemoteTipAsync(root, config, tagRef, ct).ConfigureAwait(false); }
            catch (RemoteReadException error) { return GitWriteResult.Rejected(RemoteTagText.RemoteUnreadable(TransportFailureText.Explain(error.Result)), GitOutputText.Details(error.Result)); }
            if (pushing && before == tag) return new(GitWriteOutcome.Succeeded, RemoteTagText.AlreadyThere(name, remote));
            if (pushing && before is not null) return GitWriteResult.Rejected(RemoteTagText.Different(name, remote));
            if (!pushing && before is null) return new(GitWriteOutcome.Succeeded, RemoteTagText.AlreadyGone(name, remote));
            request.Progress?.Report(pushing ? RemoteTagText.Pushing(name, remote) : RemoteTagText.Deleting(name, remote));
            var tipCheck = new RemoteTipCheck(before, tag);
            return await VerifiedGitOperation.RunAsync(
                token => pushing
                    ? transport.TransportAsync(root, config, "push", ["--porcelain", "--progress", "--no-follow-tags"], [tagRef + ":" + tagRef], token, request.Progress)
                    : transport.TransportAsync(root, config, "push", ["--porcelain", "--progress", "--no-follow-tags", "--force-with-lease=" + tagRef + ":" + before, "--delete"],
                        [tagRef], token, request.Progress),
                (command, failure, token) => VerifyAsync(request, config, tipCheck, name, command, failure, token),
                (command, error) => error is RemoteReadException read
                    ? new(GitWriteOutcome.Uncertain, RemoteTagText.NotVerified(name, remote, TransportFailureText.Explain(read.Result)), GitOutputText.Details(read.Result))
                    : new(GitWriteOutcome.Uncertain, RemoteTagText.NotVerified(name, remote, null), GitOutputText.Details(command)),
                null, SyncTimeouts.Verification, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Cancelled, RemoteTagText.Canceled(name, remote)); }
        catch (Exception error) when (GitFailures.IsExpected(error))
        { return GitWriteResult.Rejected(RemoteTagText.Failed); }
    }

    // What the local tag ref names: the tag object of an annotated tag, the commit of a lightweight one; null without it.
    private async Task<string?> LocalTagAsync(string root, string tagRef, CancellationToken ct)
    {
        var result = await local.ResultAsync(root, ["rev-parse", "--verify", "--quiet", tagRef], ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? Encoding.UTF8.GetString(result.StandardOutput).Trim() : null;
    }

    private async Task<GitWriteResult> VerifyAsync(RemoteTagRequest request, RemoteConfiguration config, RemoteTipCheck tipCheck, string name,
        GitResult? command, Exception? failure, CancellationToken verification)
    {
        var remote = request.Remote;
        // An account that could not answer stopped the push before Git ever started, so there is nothing to verify.
        if (failure is AccountException account) return GitWriteResult.Rejected(RemoteTagText.NotStarted(account.Message), account.Details);
        request.Progress?.Report(SyncOutcomeText.VerifyingRemote);
        var after = await transport.RemoteTipAsync(request.Expected.Root, config, "refs/tags/" + name, verification).ConfigureAwait(false);
        return tipCheck.Judge(after, command?.ExitCode, failure is not null) switch
        {
            RemoteTipVerdict.Arrived => new(GitWriteOutcome.Succeeded, request.Action == RemoteTagAction.Push ? RemoteTagText.Pushed(name, remote) : RemoteTagText.Deleted(name, remote)),
            RemoteTipVerdict.Interrupted => new(GitWriteOutcome.Cancelled, RemoteTagText.Interrupted(name, remote), GitOutputText.Details(command)),
            RemoteTipVerdict.Refused => GitWriteResult.Rejected(RemoteTagText.Refused(name, remote, TransportFailureText.Explain(command!)), GitOutputText.Details(command)),
            _ => new(GitWriteOutcome.Uncertain, RemoteTagText.ChangedMeanwhile(name, remote), GitOutputText.Details(command))
        };
    }
}
