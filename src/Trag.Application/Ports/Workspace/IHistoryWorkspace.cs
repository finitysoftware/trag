using Trag.Domain.Blame;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Ports.Workspace;

public interface IHistoryWorkspace
{
    Task<HistoryPage?> ReadHistoryAsync(RepositorySnapshot expected, HistoryQuery query, CancellationToken ct);
    Task<CommitDetails?> ReadCommitAsync(RepositorySnapshot expected, string id, CancellationToken ct);
    Task<FileDiff?> ReadCommitDiffAsync(RepositorySnapshot expected, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct);
    Task<FileBlame?> ReadBlameAsync(RepositorySnapshot expected, string? revision, string path, BlameOptions options, CancellationToken ct);
    Task SaveFileAsync(RepositorySnapshot expected, string commit, GitPath path, string destination, CancellationToken ct);
    Task<IReadOnlyList<GitIdentity>?> ReadAuthorsAsync(RepositorySnapshot expected, CancellationToken ct);
    Task<RepositoryDetails?> ReadDetailsAsync(RepositorySnapshot expected, CancellationToken ct);
}
