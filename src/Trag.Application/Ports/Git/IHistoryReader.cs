using Trag.Domain.Blame;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Writes;

namespace Trag.Application.Ports.Git;

public interface IHistoryReader
{
    // An unborn HEAD (null) is asked only for every branch's commits.
    Task<HistoryPage> ReadPageAsync(string root, string? head, HistoryQuery query, CancellationToken ct);
    Task<CommitDetails> ReadCommitAsync(string root, string id, CancellationToken ct);
    Task<FileDiff> ReadDiffAsync(string root, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct);
    // The file at the commit, or with no commit as the working tree has it.
    Task<FileBlame> ReadBlameAsync(string root, string? revision, string path, BlameOptions options, CancellationToken ct);
    // The file as the commit left it, written to the destination as a checkout would give it.
    Task SaveFileAsync(string root, string commit, GitPath path, string destination, CancellationToken ct);
    // The authors of the newest commits, one per commit, newest first.
    Task<IReadOnlyList<GitIdentity>> ReadAuthorsAsync(string root, CancellationToken ct);
    // What the repository's details show; a branch without commits has no history or people to read, and Git LFS files
    // are counted only where the snapshot found them.
    Task<RepositoryDetails> ReadDetailsAsync(string root, bool unborn, bool lfsFiles, CancellationToken ct);
}
