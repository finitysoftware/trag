using Trag.Application.Ports.Git;
using Trag.Domain.Blame;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

public sealed class FakeHistoryReader : IHistoryReader
{
    public List<HistoryCommit> Commits { get; } = [Snapshots.Commit(Snapshots.HeadId, "Update image", Snapshots.Id('b')), Snapshots.Commit(Snapshots.Id('b'), "Initial")];
    // Pages override the commit list: (query, page) -> page.
    public Func<string, int, HistoryPage>? Pages { get; set; }
    public Func<string, CommitDetails>? Details { get; set; }
    public Func<CommitDetails, FileDiffMetadata, bool, FileDiff>? Diff { get; set; }
    // (revision, path) -> the file blamed.
    public Func<string?, string, FileBlame>? Blames { get; set; }
    public (string? Revision, string Path)? LastBlame { get; private set; }
    public BlameOptions? LastBlameOptions { get; private set; }
    public List<(string Commit, string Path, string Destination)> Saves { get; } = [];
    public Exception? SaveFailure { get; set; }
    public int PageCalls { get; private set; }
    public int LastPageIndex { get; private set; }
    public int LastPages { get; private set; }
    public string? LastHead { get; private set; }
    public string? LastQuery { get; private set; }
    public bool? LastAllBranches { get; private set; }
    public string? LastUpstream { get; private set; }
    public string? LastPath { get; private set; }
    public int DetailCalls { get; private set; }
    public AsyncGate Gate { get; } = new();

    public async Task<HistoryPage> ReadPageAsync(string root, string? head, HistoryQuery query, CancellationToken ct)
    {
        PageCalls++; LastHead = head; LastQuery = query.Text; LastAllBranches = query.AllBranches; LastUpstream = query.Upstream; LastPath = query.Path;
        (LastPageIndex, LastPages) = (query.PageIndex, query.Pages);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        if (Pages is null) return new HistoryPage(query.PageIndex == 0 ? Commits.ToArray() : [], false, query.PageIndex);
        // Several pages read at once are their commits one after another, ending as the last of them ends.
        var read = Enumerable.Range(query.PageIndex, query.Pages).Select(page => Pages(query.Text, page)).ToArray();
        return new HistoryPage([.. read.SelectMany(page => page.Commits)], read[^1].HasMore, read[^1].Number);
    }

    public async Task<CommitDetails> ReadCommitAsync(string root, string id, CancellationToken ct)
    {
        DetailCalls++;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return Details?.Invoke(id) ?? new CommitDetails(Commits.FirstOrDefault(commit => commit.Id == id) ?? Snapshots.Commit(id, "Commit"), "Body", []);
    }

    public async Task<FileDiff> ReadDiffAsync(string root, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return Diff?.Invoke(commit, file, preview) ?? new FileDiff([file], [new('+', "+" + file.Path.Display)], DiffContentKind.Text, "");
    }

    public async Task<FileBlame> ReadBlameAsync(string root, string? revision, string path, BlameOptions options, CancellationToken ct)
    {
        (LastBlame, LastBlameOptions) = ((revision, path), options);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return Blames?.Invoke(revision, path) ?? new([]);
    }

    public List<GitIdentity> Authors { get; } = [];

    public Task<IReadOnlyList<GitIdentity>> ReadAuthorsAsync(string root, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitIdentity>>([.. Authors]);

    // The repository's details every read answers with, and what the last read was told of the snapshot.
    public RepositoryDetails RepositoryDetails { get; set; } = RepositoryDetails.None;
    public (bool Unborn, bool LfsFiles)? LastDetails { get; private set; }
    public int DetailsReads { get; private set; }
    public async Task<RepositoryDetails> ReadDetailsAsync(string root, bool unborn, bool lfsFiles, CancellationToken ct)
    {
        DetailsReads++;
        LastDetails = (unborn, lfsFiles);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return RepositoryDetails;
    }

    public Task SaveFileAsync(string root, string commit, GitPath path, string destination, CancellationToken ct)
    {
        Saves.Add((commit, path.Display, destination));
        return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
    }
}
