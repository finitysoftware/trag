using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Stash runs against the real index like a branch switch: Git owns the update and refuses anything it would overwrite.
internal sealed class StashWriter(WriteCommandRunner git, StashEntryResolver stashes)
{
    public async Task<GitWriteResult> StashAsync(StashRequest request, RepositorySnapshot before, CancellationToken ct)
    {
        var operation = request.Stash;
        return request.Action switch
        {
            StashAction.Push => await StashPushAsync(operation, before, ct).ConfigureAwait(false),
            StashAction.Apply => await StashApplyAsync(operation, before, ct).ConfigureAwait(false),
            _ => await StashDropAsync(operation, before, ct).ConfigureAwait(false)
        };
    }

    // A new stash is verified by refs/stash moving, not by Git's exit code: Git saves nothing and still exits 0 when
    // every change is in a file it does not track yet. It is verified also after Cancel: Git may have stashed the
    // changes before Cancel came.
    private async Task<GitWriteResult> StashPushAsync(StashOperation operation, RepositorySnapshot before, CancellationToken ct)
    {
        var message = operation.Message?.Trim() ?? "";
        if (operation.IncludeUntracked && await RefuseUnindexableAsync(before, ct).ConfigureAwait(false) is { } unindexable) return unindexable;
        var previous = await stashes.StashHeadAsync(before.Root, ct).ConfigureAwait(false);
        return await VerifiedGitOperation.RunAsync(
            // Without literal pathspecs on purpose: see GitCommandLine. Trag passes no pathspecs here.
            token => git.RunAsync(before.Root, PushArguments(operation, message), token, literalPathspecs: false),
            (command, _, token) => VerifyPushAsync(operation, before, previous, command, token),
            (command, _) => new(GitWriteOutcome.Uncertain, StashOutcomeText.PushNotVerified, GitOutputText.Details(command)),
            operationTimeout: null, TimeSpan.FromSeconds(15), ct,
            new StoppedWriteLock([.. StoppedWriteLock.RefStorage(before.CommonDirectory!, "refs/stash"), .. StoppedWriteLock.HeadStorage(before, "ORIG_HEAD")])).ConfigureAwait(false);
    }

    // Git for Windows skips an untracked file it cannot index, such as a reserved device name Windows 11 writes as an
    // ordinary file, saves the stash without it and then deletes it with the other untracked files. A dry run of git add
    // over the untracked files shown asks Git itself whether it can index each of them, and writes nothing; a file gone
    // since it was shown has nothing to lose. A repository inside the working tree is no such file: the stash leaves it
    // in place, and Git cannot index one without a commit.
    private async Task<GitWriteResult?> RefuseUnindexableAsync(RepositorySnapshot before, CancellationToken ct)
    {
        var untracked = before.Files.Where(file => file.IsUntracked && !file.IsRepositoryOfItsOwn).Select(file => file.Path).ToArray();
        var check = await git.RunAsync(before.Root, ["add", "--dry-run", "--ignore-missing", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, input: GitPathEncoder.Encode(untracked)).ConfigureAwait(false);
        if (check.ExitCode == 0) return null;
        // Standard output lists every file the dry run went through before it stopped; only the error says why.
        var reason = check with { StandardOutput = [] };
        return GitWriteResult.Rejected(StashFailureText.Explain(reason), GitOutputText.Details(reason));
    }

    private async Task<GitWriteResult> VerifyPushAsync(StashOperation operation, RepositorySnapshot before, string? previous, GitResult? command, CancellationToken ct)
    {
        var current = await stashes.StashHeadAsync(before.Root, ct).ConfigureAwait(false);
        // Git saves the stash before it puts the working tree back, so a Git stopped after that may have left it half done.
        if (current is not null && current != previous)
            return command is null ? new(GitWriteOutcome.Uncertain, StashOutcomeText.StashedBeforeStop)
                : new(GitWriteOutcome.Succeeded, StashOutcomeText.Stashed(operation.IncludeUntracked));
        if (command is null) return new(GitWriteOutcome.Cancelled, WriteText.Canceled);
        var details = GitOutputText.Details(command);
        if (command.ExitCode != 0) return GitWriteResult.Rejected(StashFailureText.Explain(command), details);
        // Nothing saved is an answer, not an outcome nobody can explain: it must not put the shell into review and
        // block every later write. Untracked files left out are the reason only when there are some; a change undone
        // since it was shown leaves nothing.
        if (!SavedNothing(details))
            return new(GitWriteOutcome.Uncertain, StashOutcomeText.NoNewStash, details);
        return GitWriteResult.Rejected(!operation.IncludeUntracked && before.Files.Any(file => file.IsUntracked)
            ? StashOutcomeText.OnlyUntracked
            : StashOutcomeText.NothingToStash, details);
    }

    // A stash without a message is named after its commit's subject, which Git prints in i18n.logOutputEncoding and
    // commits as UTF-8, so that is UTF-8 too.
    private static string[] PushArguments(StashOperation operation, string message)
    {
        var args = new List<string> { "-c", "i18n.logOutputEncoding=UTF-8", "stash", "push" };
        if (operation.IncludeUntracked) args.Add("--include-untracked");
        if (message.Length != 0) { args.Add("-m"); args.Add(message); }
        return args.ToArray();
    }

    private async Task<GitWriteResult> StashApplyAsync(StashOperation operation, RepositorySnapshot before, CancellationToken ct)
    {
        if (await stashes.ResolveStashAsync(operation, before.Root, ct).ConfigureAwait(false) is not { } entry) return GitWriteResult.Rejected(StashOutcomeText.Gone);
        // Git's merge of the stash writes AUTO_MERGE.
        var left = new StoppedWriteLock(StoppedWriteLock.RefStorage(before.GitDirectory!, "AUTO_MERGE"));
        var status = await StatusAsync(before.Root, ct).ConfigureAwait(false);
        GitResult command;
        try { command = await git.RunAsync(before.Root, ["stash", "apply", entry.Reference], ct).ConfigureAwait(false); }
        finally { left.Release(ct); }
        // An apply that left the status as it was is one only when the working tree holds the stash now: the branch has
        // committed its change since, or its untracked file is ignored since. Otherwise nothing was applied, whatever Git said.
        if (command.ExitCode == 0)
            return (await StatusAsync(before.Root, ct).ConfigureAwait(false)).AsSpan().SequenceEqual(status) && !await HoldsAsync(entry, before.Root, ct).ConfigureAwait(false)
                ? new(GitWriteOutcome.Uncertain, WriteText.NotVerified)
                : new(GitWriteOutcome.Succeeded, StashOutcomeText.Applied(entry.Reference));
        var text = GitOutputText.Details(command);
        if (Conflicted(text))
            return new(GitWriteOutcome.NeedsResolution, StashOutcomeText.AppliedWithConflicts(entry.Reference), text);
        return GitWriteResult.Rejected(StashFailureText.Explain(command), text);
    }

    private async Task<byte[]> StatusAsync(string root, CancellationToken ct) =>
        (await git.RunAsync(root, ["status", "--porcelain=v2", "-z", "--untracked-files=all"], ct).ConfigureAwait(false)).StandardOutput;

    // The working tree holds the stash when the stash's change, with its untracked files (the third parent, when it has
    // one), applies to it in reverse.
    private async Task<bool> HoldsAsync(StashEntry entry, string root, CancellationToken ct)
    {
        var tracked = await git.RunAsync(root, ["diff-tree", "-p", "--binary", entry.Id + "^1", entry.Id], ct).ConfigureAwait(false);
        var untracked = await git.RunAsync(root, ["diff-tree", "-p", "--binary", "--root", entry.Id + "^3"], ct).ConfigureAwait(false);
        byte[] patch = untracked.ExitCode == 0 ? [.. tracked.StandardOutput, .. untracked.StandardOutput] : tracked.StandardOutput;
        return (await git.RunAsync(root, ["apply", "--reverse", "--check", "--whitespace=nowarn", "-"], ct, input: patch).ConfigureAwait(false)).ExitCode == 0;
    }

    // The drop is verified by the list, also after Cancel: Git may have dropped the entry before Cancel came.
    private async Task<GitWriteResult> StashDropAsync(StashOperation operation, RepositorySnapshot before, CancellationToken ct)
    {
        if (await stashes.ResolveStashAsync(operation, before.Root, ct).ConfigureAwait(false) is not { } entry) return GitWriteResult.Rejected(StashOutcomeText.Gone);
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["stash", "drop", entry.Reference], token),
            (command, _, token) => VerifyDropAsync(entry, before.Root, command, token),
            (command, _) => new(GitWriteOutcome.Uncertain, StashOutcomeText.DropNotVerified, GitOutputText.Details(command)),
            operationTimeout: null, TimeSpan.FromSeconds(15), ct,
            new StoppedWriteLock(StoppedWriteLock.RefStorage(before.CommonDirectory!, "refs/stash", "packed-refs"))).ConfigureAwait(false);
    }

    private async Task<GitWriteResult> VerifyDropAsync(StashEntry entry, string root, GitResult? command, CancellationToken ct)
    {
        var remaining = await stashes.ListStashesAsync(root, ct).ConfigureAwait(false);
        // Git takes the entry off the list before it moves refs/stash, so a Git stopped after that may have left it half done.
        if (remaining.All(item => item.Id != entry.Id))
            return command is null ? new(GitWriteOutcome.Uncertain, StashOutcomeText.DropNotVerified)
                : new(GitWriteOutcome.Succeeded, StashOutcomeText.Dropped(entry.Reference, GitObjectId.Short(entry.Id, 8)));
        if (command is null) return new(GitWriteOutcome.Cancelled, WriteText.Canceled);
        // Another program changed the list after Trag read it, so the index named another stash, which Git dropped and
        // names in its output: it goes back to the list, at the top, under its commit's message.
        if (DroppedId(command) is { } other)
        {
            var subject = await git.RunAsync(root, ["show", "-s", "--no-show-signature", "--encoding=UTF-8", "--format=%s", other, "--"], ct).ConfigureAwait(false);
            await git.RunAsync(root, ["stash", "store", "-m", GitBytes.Text(subject.StandardOutput), other], ct).ConfigureAwait(false);
            return GitWriteResult.Rejected(StashOutcomeText.ListChanged);
        }
        return GitWriteResult.Rejected(StashFailureText.Explain(command), GitOutputText.Details(command));
    }

    // "Dropped stash@{1} (<id>)", in English as every Git Trag runs.
    internal static string? DroppedId(GitResult command)
    {
        var output = GitBytes.Text(command.StandardOutput);
        var open = output.LastIndexOf('(');
        var id = open >= 0 && output.EndsWith(')') ? output[(open + 1)..^1] : "";
        return GitObjectId.IsValid(id) ? id : null;
    }

    // Git's own line for a push that found nothing it stashes.
    internal static bool SavedNothing(string details) => details.Contains("No local changes to save", StringComparison.OrdinalIgnoreCase);

    // Git's own line for each file an apply left in conflict.
    internal static bool Conflicted(string details) => details.Contains("CONFLICT", StringComparison.Ordinal);
}
