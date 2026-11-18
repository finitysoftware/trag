using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class WorkspaceWriteTests
{
    private static GitWriteRequest Stage(RepositorySnapshot expected) => new StageRequest(expected, [Snapshots.Path("a.txt")]);

    [Fact]
    public async Task WhenTheWorkspaceHasNoWriter_ShouldRejectTheWrite()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task WhenTheExpectedSnapshotIsFromAnotherRoot_ShouldRejectTheWriteBeforeTheWriterRuns()
    {
        var writer = new FakeGitWriter();
        var (workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        var result = await workspace.WriteAsync(Stage(opened! with { Root = "elsewhere" }), CancellationToken.None);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenTheExpectedSnapshotIsOfARepositoryNeverOpened_ShouldRejectTheWrite()
    {
        var (workspace, _) = await OpenedAsync(new FakeGitWriter());
        await using var _ = workspace;
        Assert.Equal(GitWriteOutcome.Rejected, (await workspace.WriteAsync(Stage(Snapshots.Create("unopened")), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhenTheWriterIsInterrupted_ShouldReportAnUncertainResultWithTheRefreshedSnapshot()
    {
        var writer = new FakeGitWriter { Failure = new OperationCanceledException() };
        var (workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        var interrupted = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        Assert.Equal(GitWriteOutcome.Uncertain, interrupted.Outcome); Assert.Contains("interrupted", interrupted.Message); Assert.NotNull(interrupted.Snapshot);
    }

    // Cancel stopped Git after it had rewritten files: the writer answers canceled or refused, but the repository the
    // verification reads is not the one the write began from, so the outcome asks for a review instead.
    [Theory]
    [InlineData(GitWriteOutcome.Cancelled, true)]
    [InlineData(GitWriteOutcome.Rejected, true)]
    [InlineData(GitWriteOutcome.Cancelled, false)]
    public async Task WhenACanceledWriteEnds_ShouldAskForAReviewOnlyIfTheRepositoryChanged(GitWriteOutcome outcome, bool changed)
    {
        var reader = new FakeRepositoryReader();
        using var cancel = new CancellationTokenSource();
        var writer = new FakeGitWriter
        {
            Handle = (_, _) =>
            {
                cancel.Cancel();
                if (changed) reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
                return Task.FromResult(new GitWriteResult(outcome, "Stopped"));
            }
        };
        await using var workspace = new RepositoryWorkspace(reader, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);

        var result = await workspace.WriteAsync(Stage(opened!), cancel.Token);

        Assert.Equal(changed ? (GitWriteOutcome.Uncertain, WriteText.StoppedPartway) : (outcome, "Stopped"), (result.Outcome, result.Message));
    }

    // Only a write Cancel stopped is judged by what its verification found: one nobody stopped keeps Git's answer, also a
    // refusal after Git had changed files, as a hook that reformats files and then fails leaves it.
    [Fact]
    public async Task WhenGitRefusesAWriteNobodyStoppedAfterItChangedFiles_ShouldPassTheRefusalOn()
    {
        var reader = new FakeRepositoryReader();
        var writer = new FakeGitWriter
        {
            Handle = (_, _) =>
            {
                reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
                return Task.FromResult(GitWriteResult.Rejected("The hook refused the commit."));
            }
        };
        await using var workspace = new RepositoryWorkspace(reader, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);

        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);

        Assert.Equal((GitWriteOutcome.Rejected, "The hook refused the commit."), (result.Outcome, result.Message));
    }

    // What the writer threw may carry what the user should not see, so the message names none of it.
    [Fact]
    public async Task WhenTheWriterFails_ShouldReportAnUncertainResultWithTheRefreshedSnapshotButNotTheFailure()
    {
        var writer = new FakeGitWriter { Failure = new IOException("hook exploded") };
        var (workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        var failed = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        Assert.Equal(GitWriteOutcome.Uncertain, failed.Outcome); Assert.Contains("could not be verified", failed.Message); Assert.DoesNotContain("hook exploded", failed.Message);
        Assert.NotNull(failed.Snapshot); Assert.True(workspace.IsCurrent(failed.Snapshot!));
    }

    // A writer answers every failure Git, the file system or the output can cause with a sentence, so what it throws is a
    // defect in Trag: it is recorded with its stack where the crash logs are, and the user still gets only the sentence.
    [Fact]
    public async Task WhenTheWriterThrows_ShouldRecordWhatItThrewAsADefect()
    {
        var defect = new InvalidOperationException("defect");
        List<Exception> recorded = [];
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: new FakeGitWriter { Failure = defect }, defects: recorded.Add);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);

        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);

        Assert.Same(defect, Assert.Single(recorded));
        Assert.Equal((GitWriteOutcome.Uncertain, WriteText.NotVerified), (result.Outcome, result.Message));
    }

    [Fact]
    public async Task WhenAWriteFinishes_ShouldAskTheChangeSourceToReconcile()
    {
        var (workspace, watcher, _) = await AfterAWriteAsync();
        await using var _ = workspace;
        Assert.Equal(1, watcher.Latest.Reconciles);
    }

    [Fact]
    public async Task WhenAWriteFinishes_ShouldLetGuardedReadsAndRefreshesRunAgain()
    {
        var (workspace, _, result) = await AfterAWriteAsync();
        await using var _ = workspace;
        Assert.NotNull(await workspace.ReadBranchesAsync(result.Snapshot!, CancellationToken.None));
        Assert.NotNull(await workspace.RefreshAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(GitWriteOutcome.Rejected)]
    [InlineData(GitWriteOutcome.NeedsResolution)]
    [InlineData(GitWriteOutcome.Cancelled)]
    public async Task WhenGitAnswersWithoutSuccess_ShouldPassTheAnswerOnWithTheRefreshedSnapshot(GitWriteOutcome outcome)
    {
        var writer = new FakeGitWriter { Result = new(outcome, "Git said no", "details") };
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        Assert.Equal(outcome, result.Outcome); Assert.Equal("Git said no", result.Message); Assert.Equal("details", result.Details); Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public async Task WhenAWriteSucceeds_ShouldMakeTheVerificationReadTheCurrentSnapshot()
    {
        var (workspace, _, opened, result) = await StagedAsync();
        await using var _ = workspace;
        Assert.Equal("a.txt", Assert.Single(result.Snapshot!.Files).Path.Display);
        Assert.True(workspace.IsCurrent(result.Snapshot)); Assert.False(workspace.IsCurrent(opened!));
    }

    [Fact]
    public async Task WhenAWriteSucceeds_ShouldHaveHandedTheWriterTheSnapshotTheCallerExpected()
    {
        var (workspace, writer, opened, _) = await StagedAsync();
        await using var _ = workspace;
        Assert.Same(opened, writer.Requests.Single().Expected);
    }

    // One write holds the session at a time: a second one is refused at once and never reaches Git.
    [Fact]
    public async Task WhenASecondWriteArrivesWhileTheFirstRuns_ShouldRefuseItWithoutRunningIt()
    {
        var (workspace, writer, first, second) = await SecondWriteAskedAsync();
        await using var _ = workspace;
        Assert.True(second.IsCompleted);
        Assert.Equal(GitWriteOutcome.Rejected, (await second).Outcome);
        Assert.Single(writer.Requests);
        writer.Gate.Open();
        await first;
    }

    [Fact]
    public async Task WhenTheFirstWriteFinishes_ShouldAcceptTheNextOne()
    {
        var (workspace, writer, first, _) = await SecondWriteAskedAsync();
        await using var _ = workspace;
        writer.Gate.Open();
        var snapshot = (await first).Snapshot!;

        var next = await workspace.WriteAsync(new UnstageRequest(snapshot, [Snapshots.Path("b.txt")]), CancellationToken.None);

        Assert.Equal(GitWriteOutcome.Succeeded, next.Outcome);
        Assert.Equal([typeof(StageRequest), typeof(UnstageRequest)], writer.Requests.Select(request => request.GetType()));
    }

    // The shell cancels a quiet fetch when a write begins and sends the write at once. The writer starts only once the
    // fetch has ended, so the fetch's Git no longer holds the ref locks the write may need.
    [Fact]
    public async Task WhenAWriteBeginsWhileAQuietFetchStillRuns_ShouldStartTheWriterOnlyAfterTheFetchEnded()
    {
        var sync = new FakeGitSync();
        var writer = new FakeGitWriter();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: writer, synchronizer: sync);
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        sync.WriteGate.IsClosed = true;
        _ = workspace.FetchQuietlyAsync(opened, FakeGitSync.DefaultState(opened, "origin", null), null, CancellationToken.None);
        await sync.WriteGate.Started;
        writer.Gate.IsClosed = true;

        var writing = workspace.WriteAsync(Stage(opened), CancellationToken.None);
        var startedDuringTheFetch = await Task.WhenAny(writer.Gate.Started, Task.Delay(TimeSpan.FromMilliseconds(500))) == writer.Gate.Started;
        sync.WriteGate.Open(); writer.Gate.Open();
        await writing;

        Assert.False(startedDuringTheFetch);
    }

    private static async Task<(RepositoryWorkspace Workspace, RepositorySnapshot? Opened)> OpenedAsync(FakeGitWriter writer)
    {
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        return (workspace, opened);
    }

    // A workspace with a watcher and a branch reader, after one stage that succeeded.
    private static async Task<(RepositoryWorkspace Workspace, FakeRepositoryWatcher Watcher, GitWriteResult Result)> AfterAWriteAsync()
    {
        var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), watcher, writer: new FakeGitWriter(), branchReader: new FakeBranchReader { Local = { "main" } });
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        return (workspace, watcher, result);
    }

    // A stage whose writer leaves a.txt staged, so the read that verifies it differs from the opened snapshot.
    private static async Task<(RepositoryWorkspace Workspace, FakeGitWriter Writer, RepositorySnapshot? Opened, GitWriteResult Result)> StagedAsync()
    {
        var reader = new FakeRepositoryReader(); var writer = new FakeGitWriter();
        writer.Handle = (_, _) => { reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")]); return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Staged")); };
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var result = await workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        return (workspace, writer, opened, result);
    }

    // A stage held at the writer and an unstage accepted behind it.
    // A first write held at the writer, and a second one asked for meanwhile.
    private static async Task<(RepositoryWorkspace Workspace, FakeGitWriter Writer, Task<GitWriteResult> First, Task<GitWriteResult> Second)> SecondWriteAskedAsync()
    {
        var reader = new FakeRepositoryReader(); var writer = new FakeGitWriter { Gate = { IsClosed = true } };
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var first = workspace.WriteAsync(Stage(opened!), CancellationToken.None);
        await writer.Gate.Started;
        var second = workspace.WriteAsync(new UnstageRequest(opened!, [Snapshots.Path("b.txt")]), CancellationToken.None);
        return (workspace, writer, first, second);
    }
}
