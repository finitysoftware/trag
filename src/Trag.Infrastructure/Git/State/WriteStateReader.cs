using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.Git.State;

// The repository state a write compares, read again just before the write: HEAD, the branch, the operation, the merge
// and rebase state and the index fingerprint, plus the identity a commit stamps and the working files a discard removes.
// Every other field keeps what the last full read showed, because the read after the write replaces all of it anyway.
internal sealed class WriteStateReader(HeadReader heads, GitIdentityReader identity, PresetGitRunner git)
{
    public async Task<RepositorySnapshot> ReadAsync(GitWriteRequest request, CancellationToken ct)
    {
        var expected = request.Expected;
        if (expected.GitDirectory is null) return expected;
        var files = request is DiscardRequest ? (await WorkingTreeStatus.ReadAsync(git, expected.Root, ct).ConfigureAwait(false)).Files : expected.Files;
        var fresh = await ReadStateAsync(expected, files, ct).ConfigureAwait(false);
        if (request is not CommitRequest) return fresh;
        return fresh with
        {
            Author = await identity.ReadAsync(expected.Root, "GIT_AUTHOR_IDENT", ct).ConfigureAwait(false),
            Committer = await identity.ReadAsync(expected.Root, "GIT_COMMITTER_IDENT", ct).ConfigureAwait(false)
        };
    }

    // HEAD and the branch, the operation over the given files, the merge and rebase state and the index, read now. A reword
    // Git replayed and a hook refused is an edit stop by what the full read found at HEAD, which holds while HEAD, the
    // rebase state and the index are the ones it read, as a write requires.
    public async Task<RepositorySnapshot> ReadStateAsync(RepositorySnapshot expected, IReadOnlyList<FileStatus> files, CancellationToken ct)
    {
        if (expected.GitDirectory is not { } gitDirectory) return expected;
        var (head, branch) = await heads.ReadAsync(expected.Root, ct).ConfigureAwait(false);
        var roots = await heads.ReadRootRefsAsync(expected, ct).ConfigureAwait(false);
        // Conflicted index entries without a merge in progress are their own state, as in a full read.
        var operation = RepositoryOperations.With(RepositoryOperationDetector.Detect(gitDirectory, roots), files);
        return expected with
        {
            Head = head, Branch = branch, Operation = operation, Files = files,
            Merge = await MergeMetadataReader.ReadAsync(gitDirectory, roots, ct).ConfigureAwait(false),
            Rebase = await RebaseMetadataReader.ReadAsync(gitDirectory, roots, ct).ConfigureAwait(false) is { } rebase
                ? rebase with { OntoName = expected.Rebase?.OntoName, StoppedForEdit = rebase.StoppedForEdit || expected.Rebase is { StoppedForEdit: true } } : null,
            IndexFingerprint = await IndexFingerprint.ReadAsync(gitDirectory, ct).ConfigureAwait(false)
        };
    }
}
