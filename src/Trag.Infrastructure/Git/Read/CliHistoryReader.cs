using Trag.Application.Ports.Git;
using Trag.Domain.Blame;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The history reader the shell holds: a page of commits, one commit with its files, the diff of one of those files, a
// file blamed line by line, a commit's version of a file saved where the user chose, and the repository's details.
public sealed class CliHistoryReader : IHistoryReader
{
    private readonly HistoryPageReader pages;
    private readonly CommitDetailsReader commits;
    private readonly CommitDiffReader diffs;
    private readonly BlameReader blames;
    private readonly FileVersionReader versions;
    private readonly AuthorReader authors;
    private readonly RepositoryDetailsReader details;

    public CliHistoryReader(GitProcessRunner runner)
    {
        var git = new PresetGitRunner(runner, LocalGitPreset.Writing);
        pages = new(git);
        commits = new(git);
        diffs = new(git);
        blames = new(git);
        versions = new(git);
        authors = new(git);
        details = new(runner);
    }

    public Task<HistoryPage> ReadPageAsync(string root, string? head, HistoryQuery request, CancellationToken ct) =>
        pages.ReadPageAsync(root, head, request, ct);

    public Task<CommitDetails> ReadCommitAsync(string root, string id, CancellationToken ct) =>
        commits.ReadCommitAsync(root, id, ct);

    public Task<FileDiff> ReadDiffAsync(string root, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) =>
        diffs.ReadDiffAsync(root, commit, file, preview, ct);

    public Task<FileBlame> ReadBlameAsync(string root, string? revision, string path, BlameOptions options, CancellationToken ct) =>
        blames.ReadBlameAsync(root, revision, path, options, ct);

    public Task<IReadOnlyList<GitIdentity>> ReadAuthorsAsync(string root, CancellationToken ct) => authors.ReadAuthorsAsync(root, ct);

    public Task<RepositoryDetails> ReadDetailsAsync(string root, bool unborn, bool lfsFiles, CancellationToken ct) =>
        details.ReadAsync(root, unborn, lfsFiles, ct);

    public Task SaveFileAsync(string root, string commit, GitPath path, string destination, CancellationToken ct) =>
        versions.SaveAsync(root, commit, path, destination, ct);
}
