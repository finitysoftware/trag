using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Part of one file's changes applied to the transaction's private index: staged from the working tree's diff, or taken
// back out by applying the index's own diff in reverse. Whitespace is never judged, whatever apply.whitespace says. Git
// refuses a patch that no longer fits the index, and then nothing changes; the index is published only after the
// repository is still what it was. A discard applies the working tree's diff in reverse to the working tree alone, like a
// whole file's discard: the index is never touched, and Git converts the line endings both ways.
internal sealed class PatchWriter(WriteCommandRunner git, ExpectedStateCheck snapshots)
{
    public async Task<GitWriteResult> ApplyAsync(PatchRequest request, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        string[] arguments = request.Action == PatchAction.Unstage
            ? ["apply", "--cached", "--reverse", "--whitespace=nowarn", "-"]
            : ["apply", "--cached", "--whitespace=nowarn", "-"];
        var result = await git.RunAsync(fresh.Root, arguments, ct, transaction, Encoding.UTF8.GetBytes(request.Patch)).ConfigureAwait(false);
        if (result.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.NotTaken(request.Action, request.Lines), GitOutputText.Details(result));
        return await snapshots.PublishAsync(fresh, transaction, IndexOutcomeText.Taken(request.Action, request.Lines), ct).ConfigureAwait(false);
    }

    // Git rewrites the working file in place, and a file it was stopped in still reads as changed the same way, so a discard
    // stopped while Git runs asks for a review. Once the part is out, the file holds what the patch takes it from, so the
    // patch applies forward again; a file it does not apply to was not changed, whatever Git said, and asks for a review too.
    public async Task<GitWriteResult> DiscardAsync(PatchRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var patch = Encoding.UTF8.GetBytes(request.Patch);
        GitResult result;
        try { result = await git.RunAsync(fresh.Root, ["apply", "--reverse", "--whitespace=nowarn", "-"], ct, input: patch).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Uncertain, WriteText.Interrupted); }
        if (result.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.NotTaken(request.Action, request.Lines), GitOutputText.Details(result));
        var taken = await git.RunAsync(fresh.Root, ["apply", "--check", "--whitespace=nowarn", "-"], ct, input: patch).ConfigureAwait(false);
        return taken.ExitCode == 0
            ? new(GitWriteOutcome.Succeeded, IndexOutcomeText.Taken(request.Action, request.Lines))
            : new(GitWriteOutcome.Uncertain, WriteText.NotVerified);
    }
}
