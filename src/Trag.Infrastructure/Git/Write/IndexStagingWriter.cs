using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Staging and unstaging whole files against the transaction's private index. Staging refuses an LFS file where Git LFS
// is not installed, and a folder that is a repository of its own unless .gitmodules lists it as a submodule; a submodule
// moved to another commit stages as a file does. The index is published only after the repository is still what it was.
internal sealed class IndexStagingWriter(WriteCommandRunner git, ExpectedStateCheck snapshots)
{
    public async Task<GitWriteResult> StageAsync(IReadOnlyList<GitPath> requestPaths, bool stage, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        var paths = GitPathEncoder.Encode(requestPaths);
        if (stage && OwnChangesOnly(requestPaths, fresh) is { Length: > 0 } own) return GitWriteResult.Rejected(IndexOutcomeText.SubmoduleChangesNotStaged(string.Join(", ", own)));
        if (stage && !fresh.LfsInstalled && await RefuseLfsAsync(requestPaths, fresh, transaction, ct).ConfigureAwait(false) is { } lfs) return lfs;
        var result = await git.RunAsync(fresh.Root, Arguments(stage, fresh.Head), ct, transaction, paths).ConfigureAwait(false);
        if (result.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.NotUpdated, GitOutputText.Details(result));
        if (stage && await RefuseEmbeddedRepositoriesAsync(requestPaths, fresh, transaction, ct).ConfigureAwait(false) is { } embedded) return embedded;
        return await snapshots.PublishAsync(fresh, transaction, stage ? IndexOutcomeText.Staged : IndexOutcomeText.Unstaged, ct).ConfigureAwait(false);
    }

    // Git stages nothing of a submodule whose only changes are files inside it, since they are committed in the submodule;
    // a selection of nothing else is refused before Git, which would leave the index as it was.
    private static string[] OwnChangesOnly(IReadOnlyList<GitPath> requestPaths, RepositorySnapshot fresh)
    {
        var keys = requestPaths.Select(path => path.Key).ToHashSet(StringComparer.Ordinal);
        var selected = fresh.Files.Where(file => keys.Contains(file.Path.Key)).ToArray();
        return selected.All(file => file is { SubmoduleHasOwnChanges: true, IsSubmoduleMoved: false }) ? [.. selected.Select(file => file.Path.DisplayEscaped)] : [];
    }

    // Staging adds; unstaging restores from HEAD, or removes from the index when there is no commit to restore from.
    private static string[] Arguments(bool stage, string? head)
    {
        if (stage) return ["add", "--all", "--pathspec-from-file=-", "--pathspec-file-nul"];
        if (head is not null) return ["restore", "--staged", "--source=" + head, "--pathspec-from-file=-", "--pathspec-file-nul"];
        return ["rm", "--cached", "--force", "--ignore-unmatch", "--pathspec-from-file=-", "--pathspec-file-nul"];
    }

    // The attributes are asked before the clean filter would ever run: an LFS file is refused, not half-staged. A file
    // deleted in the working tree has no content to filter, so its deletion is staged whatever its attributes say; a
    // conflict deleted on one side keeps the other side's file on disk, so it is still asked.
    private async Task<GitWriteResult?> RefuseLfsAsync(IReadOnlyList<GitPath> requestPaths, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        var deleted = fresh.Files.Where(file => file is { IsConflict: false, Worktree: 'D' }).Select(file => file.Path.Key).ToHashSet(StringComparer.Ordinal);
        var filtered = requestPaths.Where(path => !deleted.Contains(path.Key)).ToArray();
        var attributes = await git.RunAsync(fresh.Root, ["check-attr", "-z", "--stdin", "filter"], ct, transaction, GitPathEncoder.Encode(filtered)).ConfigureAwait(false);
        var fields = NulRecordReader.Records(attributes.StandardOutput).ToArray();
        if (attributes.ExitCode != 0 || fields.Length != filtered.DistinctBy(path => path.Key).Count() * 3)
            return GitWriteResult.Rejected(IndexOutcomeText.AttributesNotVerified);
        if (GitEntries.HaveLfsFilter(fields))
            return GitWriteResult.Rejected(IndexOutcomeText.LfsNotInstalled);
        return null;
    }

    // A gitlink at a staged path is a submodule only when .gitmodules lists it; .gitmodules is asked only then. A folder
    // the status lists ends with a slash, its gitlink does not.
    private async Task<GitWriteResult?> RefuseEmbeddedRepositoriesAsync(IReadOnlyList<GitPath> requestPaths, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        var index = await git.RunAsync(fresh.Root, ["ls-files", "--stage", "-z"], ct, transaction).ConfigureAwait(false);
        if (index.ExitCode != 0) return GitWriteResult.Rejected(IndexOutcomeText.IndexNotVerified);
        var staged = requestPaths.Select(path => path.Display.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        var gitlinks = NulRecordReader.Records(index.StandardOutput).Where(row => GitEntries.IsGitlink(row))
            .Select(row => Encoding.UTF8.GetString(row.AsSpan(Array.IndexOf(row, (byte)'\t') + 1))).Where(staged.Contains).ToArray();
        if (gitlinks.Length == 0) return null;
        var listed = await git.RunAsync(fresh.Root, ["config", "--file", ".gitmodules", "--null", "--get-regexp", "^submodule\\..*\\.path$"], ct).ConfigureAwait(false);
        var submodules = new GitConfiguration(GitConfigParser.Parse(listed.StandardOutput)).Entries.Select(entry => entry.Value).ToHashSet(StringComparer.Ordinal);
        return gitlinks.All(submodules.Contains) ? null : GitWriteResult.Rejected(IndexOutcomeText.SubmodulesNotStaged);
    }
}
