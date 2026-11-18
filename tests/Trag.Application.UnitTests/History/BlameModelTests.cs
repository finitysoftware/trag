using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Blame;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.History;

// A blame read the repository moved under, after it was read: the blame says so in its own words, not a list's.
public sealed class BlameModelTests
{
    [Fact]
    public async Task WhenTheRepositoryMovesUnderTheRead_ShouldSaySoInTheBlamesWords()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Create());
        var blame = new BlameModel(repository, new MovedHistory());

        await blame.OpenAsync("a.txt", null);

        Assert.Equal(BlameText.Changed, blame.Blame.Message);
    }

    // A partial clone leaves earlier versions on its remote and Trag does not fetch them, so a blame that fails there
    // names that as the likely cause.
    [Fact]
    public async Task WhenTheBlameFailsInAPartialClone_ShouldSayEarlierVersionsMayNotBeDownloaded()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Create(limitations: [LimitationText.PartialClone]));
        var blame = new BlameModel(repository, new FailedHistory());

        await blame.OpenAsync("a.txt", null);

        Assert.Equal(BlameText.NotDownloaded, blame.Blame.Message);
    }

    // A read superseded by another answers after it: that late answer is no answer to the latest read, so the shell's
    // Cancel with nothing in flight leaves the blame following the file, and a change to it reads it again.
    [Fact]
    public async Task WhenAnOlderReadAnswersLate_ShouldKeepFollowingTheFileAfterCancel()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Create(files: [Snapshots.Modified("b.txt")]));
        var history = new HeldHistory();
        var blame = new BlameModel(repository, history);
        _ = blame.OpenAsync("a.txt", null);
        var latest = blame.UseAsync(new(IgnoreWhitespace: true, DetectMoves: false));
        history.Reads[1].SetResult(new([]));
        await latest;
        history.Reads[0].SetResult(new([]));
        await ShellHarness.Until(() => history.Reads[0].Task.IsCompleted, "the older read to answer");

        repository.Cancels.CancelAll();
        var changed = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await repository.Status.ShowAsync(changed, delta: true, CancellationToken.None, () => repository.Apply(changed),
            SnapshotChange.Of(["a.txt"]));

        Assert.Equal(3, history.Reads.Count);
    }

    // Every read waits until the test answers it, in the order the test chooses.
    private sealed class HeldHistory : IHistoryWorkspace
    {
        public List<TaskCompletionSource<FileBlame?>> Reads { get; } = [];
        public Task<HistoryPage?> ReadHistoryAsync(RepositorySnapshot expected, HistoryQuery query, CancellationToken ct) => Task.FromResult<HistoryPage?>(null);
        public Task<CommitDetails?> ReadCommitAsync(RepositorySnapshot expected, string id, CancellationToken ct) => Task.FromResult<CommitDetails?>(null);
        public Task<FileDiff?> ReadCommitDiffAsync(RepositorySnapshot expected, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) =>
            Task.FromResult<FileDiff?>(null);
        public Task<FileBlame?> ReadBlameAsync(RepositorySnapshot expected, string? revision, string path, BlameOptions options, CancellationToken ct)
        {
            var read = new TaskCompletionSource<FileBlame?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Add(read);
            return read.Task;
        }
        public Task SaveFileAsync(RepositorySnapshot expected, string commit, GitPath path, string destination, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<GitIdentity>?> ReadAuthorsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitIdentity>?>(null);
        public Task<RepositoryDetails?> ReadDetailsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<RepositoryDetails?>(null);
    }

    // Every read answers that the repository moved under it.
    private sealed class MovedHistory : IHistoryWorkspace
    {
        public Task<HistoryPage?> ReadHistoryAsync(RepositorySnapshot expected, HistoryQuery query, CancellationToken ct) => Task.FromResult<HistoryPage?>(null);
        public Task<CommitDetails?> ReadCommitAsync(RepositorySnapshot expected, string id, CancellationToken ct) => Task.FromResult<CommitDetails?>(null);
        public Task<FileDiff?> ReadCommitDiffAsync(RepositorySnapshot expected, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) =>
            Task.FromResult<FileDiff?>(null);
        public Task<FileBlame?> ReadBlameAsync(RepositorySnapshot expected, string? revision, string path, BlameOptions options, CancellationToken ct) => Task.FromResult<FileBlame?>(null);
        public Task SaveFileAsync(RepositorySnapshot expected, string commit, GitPath path, string destination, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<GitIdentity>?> ReadAuthorsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitIdentity>?>(null);
        public Task<RepositoryDetails?> ReadDetailsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<RepositoryDetails?>(null);
    }

    // Every blame fails as Git's does when it cannot read a version.
    private sealed class FailedHistory : IHistoryWorkspace
    {
        public Task<HistoryPage?> ReadHistoryAsync(RepositorySnapshot expected, HistoryQuery query, CancellationToken ct) => Task.FromResult<HistoryPage?>(null);
        public Task<CommitDetails?> ReadCommitAsync(RepositorySnapshot expected, string id, CancellationToken ct) => Task.FromResult<CommitDetails?>(null);
        public Task<FileDiff?> ReadCommitDiffAsync(RepositorySnapshot expected, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) =>
            Task.FromResult<FileDiff?>(null);
        public Task<FileBlame?> ReadBlameAsync(RepositorySnapshot expected, string? revision, string path, BlameOptions options, CancellationToken ct) =>
            Task.FromException<FileBlame?>(new InvalidOperationException("Git failed with exit code 128."));
        public Task SaveFileAsync(RepositorySnapshot expected, string commit, GitPath path, string destination, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<GitIdentity>?> ReadAuthorsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitIdentity>?>(null);
        public Task<RepositoryDetails?> ReadDetailsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<RepositoryDetails?>(null);
    }
}
