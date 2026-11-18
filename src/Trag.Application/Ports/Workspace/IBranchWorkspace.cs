using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Tags;

namespace Trag.Application.Ports.Workspace;

public interface IBranchWorkspace
{
    Task<BranchSet?> ReadBranchesAsync(RepositorySnapshot expected, CancellationToken ct);
    Task<IReadOnlyList<TagInfo>?> ReadTagsAsync(RepositorySnapshot expected, CancellationToken ct);
    Task<IReadOnlyList<StashEntry>?> ReadStashesAsync(RepositorySnapshot expected, CancellationToken ct);
    Task<bool?> ReadContainsAsync(RepositorySnapshot expected, string commit, CancellationToken ct);
    Task<int?> ReadDistanceAsync(RepositorySnapshot expected, string commit, CancellationToken ct);
    Task<IReadOnlyList<GitPath>?> ReadNotInIndexAsync(RepositorySnapshot expected, string commit, CancellationToken ct);
    Task<RewriteRange?> ReadRewriteAsync(RepositorySnapshot expected, string commit, CancellationToken ct);
    Task<string?> ReadRemoteUrlAsync(RepositorySnapshot expected, string remote, CancellationToken ct);
    Task<MergePreview?> ReadMergePreviewAsync(RepositorySnapshot expected, string into, string from, CancellationToken ct);
    Task<RebasePreview?> ReadRebasePreviewAsync(RepositorySnapshot expected, string onto, CancellationToken ct);
}
