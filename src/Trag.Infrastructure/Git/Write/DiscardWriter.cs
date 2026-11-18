using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.Git.Write;

// Discard touches only the working tree: tracked files go back to what the index holds, untracked ones are deleted, and
// a moved submodule checks out the commit the index records, and its own submodules the commits that one records, from
// what they already have. The index is never changed, so staged content survives, but for a path git add -N only
// intended to add: it holds no content, so the index forgets it and the file goes as an untracked one does. Conflicted
// files are refused by validation. A submodule's own changes belong to its own repository, so a selection with any is
// refused before anything is discarded. The discarded files' status is read again afterwards: a file that still shows its
// change was not discarded, whatever Git said.
internal sealed class DiscardWriter(WriteCommandRunner git, PresetGitRunner read)
{
    public async Task<GitWriteResult> DiscardAsync(DiscardRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var keys = request.Paths.Select(path => path.Key).ToHashSet(StringComparer.Ordinal);
        var files = fresh.Files.Where(file => file.IsWorkingTreeChange && keys.Contains(file.Path.Key)).ToArray();
        if (files.Where(file => file.SubmoduleHasOwnChanges).Select(file => file.Path.DisplayEscaped).ToArray() is { Length: > 0 } own)
            return GitWriteResult.Rejected(IndexOutcomeText.SubmoduleChangesKept(string.Join(", ", own)));
        var intended = files.Where(file => file.IsIntentToAdd).Select(file => file.Path).ToArray();
        var untracked = files.Where(file => file.IsUntracked || file.IsIntentToAdd).Select(file => file.Path).ToArray();
        var tracked = files.Where(file => !file.IsUntracked && !file.IsIntentToAdd).Select(file => file.Path).ToArray();
        var submodules = files.Where(file => file.IsSubmodule).Select(file => file.Path.Display).ToArray();
        if (tracked.Length == 0 && untracked.Length == 0) return GitWriteResult.Rejected(WriteText.SelectionStale);
        if (tracked.Length != 0)
        {
            var restore = await git.RunAsync(fresh.Root, ["restore", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, input: GitPathEncoder.Encode(tracked)).ConfigureAwait(false);
            if (restore.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.NotRestored, GitOutputText.Details(restore));
        }
        if (submodules.Length != 0)
        {
            var update = await git.RunAsync(fresh.Root, ["submodule", "update", "--checkout", "--no-fetch", "--recursive", "--", .. submodules], ct).ConfigureAwait(false);
            if (update.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.NotRestored, GitOutputText.Details(update));
        }
        if (intended.Length != 0)
        {
            var forget = await git.RunAsync(fresh.Root, ["rm", "--cached", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, input: GitPathEncoder.Encode(intended)).ConfigureAwait(false);
            if (forget.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.UntrackedNotDeleted, GitOutputText.Details(forget));
        }
        if (await CleanUntrackedAsync(fresh.Root, untracked, ct).ConfigureAwait(false) is { } notCleaned) return notCleaned;
        if (await StillChangedAsync(fresh.Root, files, ct).ConfigureAwait(false)) return new(GitWriteOutcome.Uncertain, WriteText.NotVerified);
        var count = tracked.Length + untracked.Length;
        return new(GitWriteOutcome.Succeeded, IndexOutcomeText.Discarded(count, untracked.Length != 0));
    }

    // Only the discarded files' status is read again, by their names, in bounded chunks.
    private async Task<bool> StillChangedAsync(string root, IReadOnlyList<FileStatus> files, CancellationToken ct)
    {
        foreach (var chunk in PathBatches.Of(files.Select(file => ":(literal)" + file.Path.Display)))
            if ((await WorkingTreeStatus.PathsAsync(read, root, chunk, ct).ConfigureAwait(false)).Any(file => file.IsWorkingTreeChange)) return true;
        return false;
    }

    // git clean takes no pathspec file: names go on the command line, literally (the runner passes --literal-pathspecs),
    // in bounded chunks. The validator refused a name that is not its own UTF-8 before anything was discarded.
    private async Task<GitWriteResult?> CleanUntrackedAsync(string root, IReadOnlyList<GitPath> untracked, CancellationToken ct)
    {
        foreach (var chunk in untracked.Select(path => path.Display).Chunk(16))
        {
            var clean = await git.RunAsync(root, ["clean", "--force", "--quiet", "--", .. chunk], ct).ConfigureAwait(false);
            if (clean.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.UntrackedNotDeleted, GitOutputText.Details(clean));
        }
        return null;
    }
}
