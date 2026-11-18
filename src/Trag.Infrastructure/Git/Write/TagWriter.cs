using System.Text;
using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// A tag created at a commit, or deleted, touches neither HEAD nor the index: a create has Git check the name first and
// refuses a commit that is gone and a name already taken, a delete refuses a tag that is gone and hands Git the name
// after "--", since a tag another program made may start with '-', and the result is verified by what the tag points at.
// A message is handed over as it was typed, its last line ended, so Git keeps an annotated tag with it; without one the
// tag is a lightweight one. No editor ever opens: where Git still wants a message (a tag it signs, by a setting Trag did
// not see yet) it is refused in Trag's words.
internal sealed class TagWriter(WriteCommandRunner git, GitRefNameValidator refNames)
{

    public async Task<GitWriteResult> CreateAsync(TagRequest request, RepositorySnapshot before, CancellationToken ct)
    {
        var name = request.Tag.Name.Trim();
        var commit = request.Tag.Commit;
        if (!await IsValidNameAsync(before.Root, name, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(TagOutcomeText.NameInvalid);
        if (!await git.RefExistsAsync(before.Root, commit + "^{commit}", ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(HistoryText.CommitGone);
        if (await git.RefExistsAsync(before.Root, "refs/tags/" + name, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(TagOutcomeText.NameTaken(name));
        var message = request.Tag.Message;
        // --file alone makes the tag an annotated one; --annotate would also tell Git not to sign it under tag.forceSignAnnotated.
        return await VerifiedGitOperation.RunAsync(
            token => message.Trim().Length == 0
                ? git.RunAsync(before.Root, ["tag", name, commit], token, variables: WriteCommandRunner.NoEditor)
                : git.RunAsync(before.Root, ["tag", "--file=-", "--cleanup=verbatim", name, commit], token, input: Encoding.UTF8.GetBytes(Ended(MessageLines.ForGit(message)))),
            async (command, failure, token) => await MarkedAsync(before.Root, name, token).ConfigureAwait(false) switch
            {
                { } marked when marked == commit => new(GitWriteOutcome.Succeeded, TagOutcomeText.Created(name, GitObjectId.Short(commit))),
                { } => new(GitWriteOutcome.Uncertain, TagOutcomeText.NotVerified, GitOutputText.Details(command)),
                null when command is not null && CommitSignings.TagRefused(GitOutputText.Details(command)) =>
                    GitWriteResult.Rejected(TagOutcomeText.NotSigned(before.SigningKey), GitOutputText.Details(command)),
                null when command is not null && CommitSignings.TagNeedsMessage(GitOutputText.Details(command)) =>
                    GitWriteResult.Rejected(TagOutcomeText.NeedsMessage, GitOutputText.Details(command)),
                null => Unchanged(command, failure, TagOutcomeText.Failed, TagOutcomeText.Interrupted)
            },
            (command, _) => new(GitWriteOutcome.Uncertain, TagOutcomeText.NotVerified, GitOutputText.Details(command)),
            operationTimeout: null, TimeSpan.FromSeconds(15), ct, new StoppedWriteLock(StoppedWriteLock.RefStorage(before.CommonDirectory!, "refs/tags/" + name)))
            .ConfigureAwait(false);
    }

    public async Task<GitWriteResult> DeleteAsync(TagDeleteRequest request, RepositorySnapshot before, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await MarkedAsync(before.Root, name, ct).ConfigureAwait(false) is not { } marked)
            return GitWriteResult.Rejected(TagOutcomeText.Gone(name));
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["tag", "--delete", "--", name], token),
            async (command, failure, token) => await git.RefExistsAsync(before.Root, "refs/tags/" + name, token).ConfigureAwait(false)
                ? Unchanged(command, failure, TagOutcomeText.DeleteFailed, TagOutcomeText.DeleteInterrupted)
                : new(GitWriteOutcome.Succeeded, TagOutcomeText.Deleted(name, GitObjectId.Short(marked))),
            (command, _) => new(GitWriteOutcome.Uncertain, TagOutcomeText.DeleteNotVerified, GitOutputText.Details(command)),
            operationTimeout: null, TimeSpan.FromSeconds(15), ct,
            new StoppedWriteLock(StoppedWriteLock.RefStorage(before.CommonDirectory!, "refs/tags/" + name, "packed-refs"))).ConfigureAwait(false);
    }

    // Git appends a tag's signature right after its message and finds one only at the start of a line.
    private static string Ended(string message) => message.EndsWith('\n') ? message : message + "\n";

    private Task<bool> IsValidNameAsync(string root, string name, CancellationToken ct) => refNames.IsValidRefAsync(root, "refs/tags/" + name, false, ct);

    // The commit the tag marks; null without the tag.
    private Task<string?> MarkedAsync(string root, string name, CancellationToken ct) => git.ResolveAsync(root, "refs/tags/" + name + "^{commit}", ct);

    // The tag is as it was: when Git ran to the end that is Git's refusal, with its output; otherwise the command was
    // interrupted before it changed anything.
    private static GitWriteResult Unchanged(GitResult? command, Exception? failure, string failed, string interrupted) =>
        failure is null && command is not null
            ? GitWriteResult.Rejected(failed, GitOutputText.Details(command))
            : new(GitWriteOutcome.Cancelled, interrupted);
}
