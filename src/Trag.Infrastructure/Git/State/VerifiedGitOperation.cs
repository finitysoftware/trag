using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.State;

// The shape every Git write has, in the order that makes an interrupted write answerable. The command runs under a
// timeout of its own; whatever stopped it is kept rather than thrown, because Git may already have done the work. The
// verification then runs on a fresh token, since the caller's is gone by the time it matters, and a verification that
// cannot finish is itself an answer. What is verified, and every sentence, belongs to the operation; only this is here.
// A command that writes the index or refs brings its StoppedWriteLock, released when Cancel or its own limit stops it.
internal static class VerifiedGitOperation
{
    public static async Task<GitWriteResult> RunAsync(
        Func<CancellationToken, Task<GitResult>> run,
        Func<GitResult?, Exception?, CancellationToken, Task<GitWriteResult>> verify,
        Func<GitResult?, Exception, GitWriteResult> recover,
        TimeSpan? operationTimeout, TimeSpan verificationTimeout, CancellationToken ct, StoppedWriteLock? left = null)
    {
        GitResult? command = null;
        Exception? failure = null;
        try
        {
            // No timeout of its own means the command runs on the caller's token, for an operation that is expected
            // to be immediate, or may wait for the user, and has nothing useful to say about a limit.
            using var timeout = operationTimeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout?.CancelAfter(operationTimeout!.Value);
            var token = timeout?.Token ?? ct;
            // A Git the limit stops is killed as one Cancel stops, so the locks it held go as they do then.
            try { command = await run(token).ConfigureAwait(false); }
            finally { left?.Release(token); }
        }
        catch (Exception error) { failure = error; }
        using var recovery = new CancellationTokenSource(verificationTimeout);
        try { return await verify(command, failure, recovery.Token).ConfigureAwait(false); }
        catch (Exception error) { return recover(command, error); }
    }
}
