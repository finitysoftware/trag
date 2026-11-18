using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Write;

// A commit's version of one file in the working tree, the file named to Git as text, which the validator has made sure
// of; the index is never touched, so what is staged stays staged.
// Restore gives the file the content the commit left it with; a file that already has it is left alone, and the result
// says so rather than claiming a restore.
// Revert undoes what the commit did to the file: the commit's diff of it, against its first parent as its file list
// compares it (nothing for the first commit), applied in reverse. A rename is read as one, so undoing it keeps the later
// changes too, and Git converts the line endings both ways. A file changed since in the same lines is refused, not
// merged, and then nothing changes; a file that already has what the commit replaced says so, and so does a commit that
// only changed the executable bit where the working tree keeps none.
internal sealed class FileVersionWriter(WriteCommandRunner git)
{
    public async Task<GitWriteResult> RestoreAsync(RestoreFileRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var differs = await git.RunAsync(fresh.Root, ["diff", "--quiet", request.Commit, "--", request.Path.Display], ct).ConfigureAwait(false);
        if (differs.ExitCode == 0) return new(GitWriteOutcome.Succeeded, FileVersionText.AlreadyRestored(request.Path.Display, GitObjectId.Short(request.Commit)));
        var restore = await git.RunAsync(fresh.Root, ["restore", "--source=" + request.Commit, "--", request.Path.Display], ct).ConfigureAwait(false);
        if (restore.ExitCode != 0) return GitWriteResult.Rejected(FileVersionText.NotRestored, GitOutputText.Details(restore));
        var restored = await git.RunAsync(fresh.Root, ["diff", "--quiet", request.Commit, "--", request.Path.Display], ct).ConfigureAwait(false);
        return restored.ExitCode == 0
            ? new(GitWriteOutcome.Succeeded, FileVersionText.Restored(request.Path.Display, GitObjectId.Short(request.Commit)))
            : new(GitWriteOutcome.Uncertain, WriteText.NotVerified);
    }

    public async Task<GitWriteResult> RevertAsync(RevertFileRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        string[] paths = request.OriginalPath is { } original ? [original.Display, request.Path.Display] : [request.Path.Display];
        // Git cannot write a file over a folder, and undoing a rename it would already have removed the new name.
        if (paths.FirstOrDefault(path => Directory.Exists(Path.Combine(fresh.Root, path))) is { } folder)
            return GitWriteResult.Rejected(FileVersionText.FolderInTheWay(folder));
        var patch = await git.RunAsync(fresh.Root, ["diff-tree", "-p", "-M", "--binary", "--root", "--diff-merges=first-parent", request.Commit, "--", .. paths], ct).ConfigureAwait(false);
        if (patch.ExitCode != 0) return GitWriteResult.Rejected(FileVersionText.RevertCommitGone, GitOutputText.Details(patch));
        // Git applies a patch that only sets or clears the executable bit to a working tree that keeps no such bit without
        // changing anything.
        if (SetsOnlyTheExecutableBit(patch.StandardOutput)
            && GitBytes.Text((await git.RunAsync(fresh.Root, ["config", "--type=bool", "--get", "core.fileMode"], ct).ConfigureAwait(false)).StandardOutput) == "false")
            return GitWriteResult.Rejected(FileVersionText.ExecutableBitNotKept(request.Path.Display, GitObjectId.Short(request.Commit)));
        var apply = await git.RunAsync(fresh.Root, ["apply", "--reverse", "--whitespace=nowarn", "-"], ct, input: patch.StandardOutput).ConfigureAwait(false);
        // The patch still applies forward when the file already has the lines the commit replaced: after the revert, or
        // before it when there was nothing to undo.
        var undone = await git.RunAsync(fresh.Root, ["apply", "--check", "--whitespace=nowarn", "-"], ct, input: patch.StandardOutput).ConfigureAwait(false);
        if (apply.ExitCode == 0)
            return undone.ExitCode == 0 ? new(GitWriteOutcome.Succeeded, FileVersionText.Reverted(request.Path.Display, GitObjectId.Short(request.Commit)))
                : new(GitWriteOutcome.Uncertain, WriteText.NotVerified);
        return undone.ExitCode == 0
            ? GitWriteResult.Rejected(FileVersionText.AlreadyReverted(request.Path.Display, GitObjectId.Short(request.Commit)))
            : GitWriteResult.Rejected(FileVersionText.NotReverted, GitOutputText.Details(apply));
    }

    // Such a patch is the commit's ID, the file's header, the old mode and the new mode, and nothing else.
    private static bool SetsOnlyTheExecutableBit(byte[] patch) =>
        GitBytes.Text(patch).Split('\n') is [_, _, var mode, _] && mode.StartsWith("old mode ", StringComparison.Ordinal);
}
