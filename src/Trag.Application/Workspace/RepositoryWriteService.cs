using Trag.Application.Ports.Git;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Workspace;

// Runs one write at a time on the session, off the caller's thread, keeps the session held until the result is verified,
// and asks the change source to reconcile afterwards so the watcher catches up with what the write changed. A write asked
// for while another holds the session is refused. What the writer throws is a defect, since it answers every failure
// Git or the file system can cause: it goes to the defects record, and the write is reported as not verified.
public sealed class RepositoryWriteService(IGitWriter writer, WorkspaceState state, WriteVerification verification, Action<Exception> defects)
{
    public async Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken ct)
    {
        if (!state.TryBeginWrite(request.Expected.Root, out var slot))
            return GitWriteResult.Rejected(WriteText.SessionChanged);
        try
        {
            return await Task.Run(() => ExecuteAndVerifyAsync(slot, request, ct), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // A read that began before the write and answers after it speaks for a state the write replaced.
            slot.Lease.Invalidate();
            // The source may be retired between the lock and the call; a retired source ignores it.
            state.EndWrite(slot)?.Reconcile();
        }
    }

    private async Task<GitWriteResult> ExecuteAndVerifyAsync(RepositorySlot slot, GitWriteRequest request, CancellationToken ct)
    {
        var result = await ExecuteAsync(request, ct).ConfigureAwait(false);
        // Once execution starts, even a cancellation or rejection needs an independent final read, and a write Cancel
        // stopped is judged by what that read found.
        var verified = await verification.VerifyAsync(slot, result).ConfigureAwait(false);
        return ct.IsCancellationRequested ? verified.AfterStop(request.Expected) : verified;
    }

    private async Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct)
    {
        try
        {
            return await writer.ExecuteAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(GitWriteOutcome.Uncertain, WriteText.Interrupted);
        }
        catch (Exception error)
        {
            defects(error);
            return new(GitWriteOutcome.Uncertain, WriteText.NotVerified);
        }
    }
}
