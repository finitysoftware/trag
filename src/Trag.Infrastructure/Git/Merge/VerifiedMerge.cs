using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Infrastructure.Git.Sync;
using Trag.Domain.Merge;

namespace Trag.Infrastructure.Git.Merge;

// A merge of one commit into the current branch, for a pull and for a branch merge alike. It runs against a private index
// and publishes it only for an outcome it has verified: a fast-forward, a merge commit whose parents and tree match, or a
// merge left for the user to resolve. Anything else keeps the index for review. The merge and write-tree against that
// private index run without the local runner's failure rule, because a non-zero exit is an answer for both.
internal sealed class VerifiedMerge(IRepositoryReader reader, PresetGitRunner git)
{
    private readonly LfsTreeInspector lfsFiles = new(git);

    public async Task<GitWriteResult> MergeAsync(MergeCommand command, CancellationToken ct)
    {
        var (before, target, kind) = (command.Before, command.Target, command.Kind);
        GitObjectId.Validate(target);
        // Without Git LFS, Git would check its files out as pointers and commit them as content; with it, the merge's
        // checkout takes their content from the local storage, or downloads it, and so runs without a time limit.
        var incomingLfs = await lfsFiles.HasLfsAsync(before.Root, target, ct).ConfigureAwait(false);
        if (!before.LfsInstalled && incomingLfs)
            return GitWriteResult.Rejected(MergeOutcomeText.IncomingLfs(kind.Operation));
        var transaction = IndexTransaction.TryBegin(before.GitDirectory!);
        if (transaction is null) return GitWriteResult.Rejected(WriteText.IndexLockHeld);
        await using (transaction)
        {
            var fresh = await reader.ReadAsync(before.Root, ct).ConfigureAwait(false);
            if (!SnapshotComparison.ReadyForMerge(before, fresh))
                return GitWriteResult.Rejected(kind.Text.Changed);
            if (await IgnoredInTheWayAsync(fresh, target, ct).ConfigureAwait(false) is { Length: > 0 } ignored)
                return new(GitWriteOutcome.Rejected, MergeOutcomeText.IgnoredInTheWay, ignored);
            await transaction.CopyAsync(ct).ConfigureAwait(false);
            var env = new Dictionary<string, string?>
            {
                ["GIT_INDEX_FILE"] = transaction.TemporaryIndex, ["GIT_MERGE_AUTOEDIT"] = "no", ["GIT_REFLOG_ACTION"] = kind.Reflog + transaction.Id
            };
            return await VerifiedGitOperation.RunAsync(
                token => git.ResultAsync(before.Root, Arguments(command), token, env, literalPathspecs: false),
                (result, failure, token) =>
                {
                    command.Progress?.Report(MergeOutcomeText.Verifying);
                    return VerifyMergeAsync(command, transaction, env, result, failure, ct, token);
                },
                (_, _) => transaction.KeepForReview(kind.Text.Unsynchronized),
                incomingLfs ? null : OperationTimeLimit.Of(fresh, SyncTimeouts.Merge), SyncTimeouts.Verification, ct,
                new StoppedWriteLock(StoppedWriteLock.HeadStorage(before, "ORIG_HEAD")))
                .ConfigureAwait(false);
        }
    }

    // The branch's own merge options are cleared, so what runs is exactly this list. A branch merge names its fast-forward
    // choice and a pull fast-forwards where it can; both bring their message, without the list of merged commits merge.log
    // adds.
    private static string[] Arguments(MergeCommand command) =>
    [
        "-c", "submodule.recurse=false", "-c", "branch." + command.Before.Branch + ".mergeOptions=",
        "merge", command.NoFastForward ? "--no-ff" : "--ff", "--commit", "--no-squash", "--no-autostash", "--no-edit",
        "--no-log", "--cleanup=verbatim", "-m", MessageLines.ForGit(command.Message),
        "--strategy=ort", "--no-rerere-autoupdate", "--no-overwrite-ignore", "--", command.Target
    ];

    // An ort merge replaces an ignored file or folder in spite of --no-overwrite-ignore: one at the path of a file the
    // merge adds to HEAD, or a file where that path needs a folder. Those here that Git ignores are named before Git
    // starts, one per line. merge-tree writes the merge's tree also when it conflicts; a merge it cannot make at all is
    // left to Git's own answer.
    private async Task<string> IgnoredInTheWayAsync(RepositorySnapshot fresh, string target, CancellationToken ct)
    {
        var merged = await git.ResultAsync(fresh.Root, ["merge-tree", "--write-tree", "-z", "--name-only", "--no-messages", fresh.Head!, target], ct).ConfigureAwait(false);
        if (merged.ExitCode > 1) return "";
        var tree = GitBytes.StrictText(NulRecordReader.Records(merged.StandardOutput).First());
        var added = await git.RunAsync(fresh.Root, ["diff-tree", "-r", "-z", "--name-only", "--no-renames", "--diff-filter=A", fresh.Head!, tree], ct).ConfigureAwait(false);
        var inTheWay = NulRecordReader.Records(added.StandardOutput).SelectMany(path => InTheWay(fresh.Root, new GitPath(path).Display)).Distinct()
            .SelectMany(path => Encoding.UTF8.GetBytes(path + "\0")).ToArray();
        var ignored = await git.ResultAsync(fresh.Root, ["check-ignore", "--stdin", "-z"], ct, input: inTheWay, literalPathspecs: false).ConfigureAwait(false);
        return string.Join("\n", NulRecordReader.Records(ignored.StandardOutput).Select(path => new GitPath(path).DisplayEscaped));
    }

    // Every folder of the path that is a file here, and the path itself when anything is there.
    private static IEnumerable<string> InTheWay(string root, string path)
    {
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            if (File.Exists(Path.Combine(root, path[..slash]))) yield return path[..slash];
        if (Path.Exists(Path.Combine(root, path))) yield return path;
    }

    // Verification has its own deadline; the caller's token only describes the completed result. The questions are
    // asked in order: a merge left for the user, a fast-forward, a merge commit, nothing changed, or review.
    private async Task<GitWriteResult> VerifyMergeAsync(MergeCommand command, IndexTransaction transaction,
        Dictionary<string, string?> env, GitResult? result, Exception? failure, CancellationToken ct, CancellationToken verification)
    {
        var (before, target, kind) = (command.Before, command.Target, command.Kind);
        var actual = await reader.ReadAsync(before.Root, verification).ConfigureAwait(false);
        var state = new MergeStateCheck(before, actual, target);
        if (state.LeftPendingMerge(result?.ExitCode, failure is not null))
        {
            // A merge commit Git could not sign leaves the merge ready to finish, with nothing to resolve.
            var pending = CommitSignings.Refused(actual.Signing, GitOutputText.Details(result)) ? MergeOutcomeText.NotSigned(actual.Signing) : kind.Text.Pending;
            return await PublishPendingMergeAsync(before.Root, transaction, env, result!, pending, verification).ConfigureAwait(false);
        }

        var interrupted = failure is not null || ct.IsCancellationRequested;
        var indexResult = await git.ResultAsync(before.Root, ["write-tree"], verification, env).ConfigureAwait(false);
        var indexTree = indexResult.ExitCode == 0 ? GitBytes.StrictText(indexResult.StandardOutput) : "";
        var incomingTree = await ReadTreeAsync(before.Root, target, verification).ConfigureAwait(false);
        if (state.IsFastForward(indexTree, incomingTree))
        {
            await transaction.PublishAsync(verification).ConfigureAwait(false);
            return new(GitWriteOutcome.Succeeded, interrupted ? MergeOutcomeText.FastForwardAfterInterruption : kind.Text.FastForward);
        }
        if (await IsMergeCommitAsync(state, indexTree, verification).ConfigureAwait(false))
        {
            await FinishMergeMetadataAsync(state, env, verification).ConfigureAwait(false);
            await transaction.PublishAsync(verification).ConfigureAwait(false);
            return new(GitWriteOutcome.Succeeded, interrupted ? MergeOutcomeText.MergeCommitAfterInterruption : kind.Text.MergeCommit, CommitId: actual.Head);
        }
        var originalTree = await ReadTreeAsync(before.Root, before.Head, verification).ConfigureAwait(false);
        if (state.IsUnchanged(indexTree, originalTree) && result?.ExitCode != 0)
            return UnchangedResult(result, failure, kind);
        return transaction.KeepForReview(kind.Text.Review);
    }

    private async Task<GitWriteResult> PublishPendingMergeAsync(string root, IndexTransaction transaction,
        Dictionary<string, string?> env, GitResult result, string message, CancellationToken ct)
    {
        // Git reads the pending index whole before it becomes the repository's; a failed read throws.
        await git.RunAsync(root, ["ls-files", "--stage", "-z"], ct, variables: env).ConfigureAwait(false);
        await transaction.PublishAsync(ct).ConfigureAwait(false);
        return new(GitWriteOutcome.NeedsResolution, message, GitOutputText.Details(result));
    }

    private async Task<string> ReadTreeAsync(string root, string? commit, CancellationToken ct)
    {
        var result = await git.RunAsync(root, ["rev-parse", commit + "^{tree}"], ct).ConfigureAwait(false);
        return GitBytes.StrictText(result.StandardOutput);
    }

    private async Task<bool> IsMergeCommitAsync(MergeStateCheck state, string indexTree, CancellationToken ct)
    {
        if (!state.CouldBeMergeCommit(indexTree)) return false;
        var result = await git.RunAsync(state.Before.Root, ["show", "-s", "--no-show-signature", "--format=%P%x00%T", state.Actual.Head!, "--"], ct).ConfigureAwait(false);
        var commit = GitBytes.StrictText(result.StandardOutput).Split('\0');
        return state.MatchesCommit(commit, indexTree);
    }

    private async Task FinishMergeMetadataAsync(MergeStateCheck state, Dictionary<string, string?> env, CancellationToken ct)
    {
        if (!state.NeedsMetadataCleanup)
        {
            if (state.Actual.Operation != RepositoryOperation.None) throw new IOException("Another repository operation is active.");
            return;
        }
        // A canceled post-merge hook can leave metadata after HEAD was committed. Quit only the verified merge.
        var root = state.Before.Root;
        var rechecked = await reader.ReadAsync(root, ct).ConfigureAwait(false);
        if (!SnapshotComparison.SameStateForSync(state.Actual, rechecked) || state.Actual.Merge != rechecked.Merge)
            throw new IOException("Merge state changed during verification.");
        await git.RunAsync(root, ["merge", "--quit"], ct, variables: env).ConfigureAwait(false);
        var finished = await reader.ReadAsync(root, ct).ConfigureAwait(false);
        if (!state.HasFinishedMerge(finished)) throw new IOException("Merge metadata cleanup could not be verified.");
    }

    private static GitWriteResult UnchangedResult(GitResult? result, Exception? failure, MergeKind kind)
    {
        var interrupted = failure is not null;
        return new(interrupted ? GitWriteOutcome.Cancelled : GitWriteOutcome.Rejected,
            interrupted ? kind.Text.Stopped : MergeOutcomeText.Rejected,
            GitOutputText.Details(result));
    }
}
