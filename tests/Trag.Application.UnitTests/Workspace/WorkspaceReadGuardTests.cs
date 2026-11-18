using Trag.Application.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

// Every secondary read (history, commit, commit diff, repository details, branches, stashes, sync, working diff) is guarded by the same rules:
// no reader, a mutation in flight, a snapshot from another history or a replaced session all mean "no answer".
public sealed class WorkspaceReadGuardTests
{
    public enum Read { History, Commit, CommitDiff, Details, Branches, Stashes, Sync, Diff }
    public static TheoryData<Read> AllReads => [.. Enum.GetValues<Read>()];
    public static TheoryData<Read> HistoryGuardedReads => [Read.History, Read.Commit, Read.CommitDiff, Read.Details, Read.Branches, Read.Stashes, Read.Sync];

    private static readonly FileDiffMetadata File = new(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    private sealed class Backends
    {
        public FakeRepositoryReader Reader { get; } = new();
        public FakeGitWriter Writer { get; } = new();
        public FakeHistoryReader History { get; } = new();
        public FakeBranchReader Branches { get; } = new() { Local = { "main" } };
        public FakeStashReader Stashes { get; } = new();
        public FakeGitSync Sync { get; } = new();
        public FakeFileDiffReader Diff { get; } = new();
        public RepositoryWorkspace Workspace() => new(Reader, diffReader: Diff, writer: Writer, historyReader: History, synchronizer: Sync, branchReader: Branches, stashReader: Stashes);
        public AsyncGate Gate(Read read) => read switch
        {
            Read.History or Read.Commit or Read.CommitDiff or Read.Details => History.Gate,
            Read.Branches => Branches.Gate,
            Read.Stashes => Stashes.Gate,
            Read.Sync => Sync.ReadGate,
            _ => Diff.Gate
        };
    }

    private static async Task<object?> Execute(RepositoryWorkspace workspace, Read read, RepositorySnapshot expected, CancellationToken ct = default) => read switch
    {
        Read.History => await workspace.ReadHistoryAsync(expected, new(), ct),
        Read.Commit => await workspace.ReadCommitAsync(expected, Snapshots.HeadId, ct),
        Read.CommitDiff => await workspace.ReadCommitDiffAsync(expected, new(Snapshots.Commit(Snapshots.HeadId, "x"), "", [File]), File, false, ct),
        Read.Details => await workspace.ReadDetailsAsync(expected, ct),
        Read.Branches => await workspace.ReadBranchesAsync(expected, ct),
        Read.Stashes => await workspace.ReadStashesAsync(expected, ct),
        Read.Sync => await workspace.ReadSyncAsync(expected, "origin", null, ct),
        _ => await workspace.ReadDiffAsync(Snapshots.Modified("a.txt"), false, false, ct)
    };

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheWorkspaceHasNoReaderForIt_ShouldAnswerNull(Read read)
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        Assert.Null(await Execute(workspace, read, opened!));
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheReadIsForTheCurrentSnapshot_ShouldAnswer(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        Assert.NotNull(await Execute(workspace, read, opened!));
    }

    [Theory, MemberData(nameof(HistoryGuardedReads))]
    public async Task WhenTheSnapshotIsFromAnotherRoot_ShouldAnswerNull(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        Assert.Null(await Execute(workspace, read, opened! with { Root = "elsewhere" }));
    }

    [Theory, MemberData(nameof(HistoryGuardedReads))]
    public async Task WhenTheSnapshotHasAnotherHead_ShouldAnswerNull(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        Assert.Null(await Execute(workspace, read, opened with { Head = Snapshots.Id('b') }));
    }

    [Theory, MemberData(nameof(HistoryGuardedReads))]
    public async Task WhenTheSnapshotIsOnAnotherBranch_ShouldAnswerNull(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        Assert.Null(await Execute(workspace, read, opened with { Branch = "other" }));
    }

    [Theory, MemberData(nameof(HistoryGuardedReads))]
    public async Task WhenTheSnapshotDiffersOnlyInWorktreeChanges_ShouldStillAnswer(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        Assert.NotNull(await Execute(workspace, read, opened with { Files = [Snapshots.Modified("x")] }));
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenAWriteIsInFlight_ShouldAnswerNull(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var write = await WriteHeldAsync(backends, workspace, opened!);
        Assert.Null(await Execute(workspace, read, opened!));
        backends.Writer.Gate.Open();
        await write;
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheWriteInFlightHasFinished_ShouldAnswerForTheSnapshotItLeft(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var write = await WriteHeldAsync(backends, workspace, opened!);
        backends.Writer.Gate.Open();
        var result = await write;
        Assert.NotNull(await Execute(workspace, read, result.Snapshot!));
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenAWriteStartsDuringTheRead_ShouldDiscardItsResult(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var gate = backends.Gate(read); gate.IsClosed = true;
        var reading = Execute(workspace, read, opened!);
        await gate.Started;
        backends.Writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        gate.Open();
        Assert.Null(await reading);
        backends.Writer.Gate.Open();
        await write;
    }

    // The working diff answers nothing for a read the write overtook; the other reads are cancelled instead.
    [Fact]
    public async Task WhenAWriteKeepingTheHeadCompletesDuringAWorkingDiffRead_ShouldAnswerNull()
    {
        var (workspace, opened, result, reading) = await WriteCompletedDuringAReadAsync(Read.Diff);
        await using var _ = workspace;
        Assert.Equal(opened.Head, result.Snapshot!.Head);
        Assert.Null(await reading);
    }

    [Theory, MemberData(nameof(HistoryGuardedReads))]
    public async Task WhenAWriteKeepingTheHeadCompletesDuringARead_ShouldCancelTheRead(Read read)
    {
        var (workspace, opened, result, reading) = await WriteCompletedDuringAReadAsync(read);
        await using var _ = workspace;
        Assert.Equal(opened.Head, result.Snapshot!.Head);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenAWriteKeepingTheHeadCompletesDuringARead_ShouldMoveOnToTheSnapshotItLeft(Read read)
    {
        var (workspace, opened, result, _) = await WriteCompletedDuringAReadAsync(read);
        await using var _ = workspace;
        Assert.Equal(opened.Head, result.Snapshot!.Head);
        Assert.True(workspace.IsCurrent(result.Snapshot));
        Assert.NotNull(await Execute(workspace, read, result.Snapshot));
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenDisposalWaitsForAnAcceptedWrite_ShouldRefuseTheReadAndARefresh(Read read)
    {
        var backends = new Backends();
        var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", default))!;
        var write = await WriteHeldAsync(backends, workspace, opened);
        var disposing = workspace.DisposeAsync().AsTask();
        try
        {
            Assert.Null(await Execute(workspace, read, opened));
            Assert.Null(await workspace.RefreshAsync(default));
            Assert.Equal(1, backends.Reader.Calls);
        }
        finally
        {
            backends.Writer.Gate.Open();
            await write;
            await disposing;
        }
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheCallerCancelsARead_ShouldDiscardItsResultEvenIfTheReaderIgnoresTheCancel(Read read)
    {
        using var cancellation = new CancellationTokenSource();
        var (workspace, _, reading) = await ReadCancelledByItsCallerAsync(read, cancellation);
        await using var _ = workspace;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheCallerHasCancelledARead_ShouldKeepTheSessionAndAnswerTheNextRead(Read read)
    {
        using var cancellation = new CancellationTokenSource();
        var (workspace, opened, reading) = await ReadCancelledByItsCallerAsync(read, cancellation);
        await using var _ = workspace;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.True(workspace.IsCurrent(opened));
        Assert.NotNull(await Execute(workspace, read, opened));
    }

    [Theory, MemberData(nameof(AllReads))]
    public async Task WhenTheSessionIsReplacedDuringTheRead_ShouldCancelIt(Read read)
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var gate = backends.Gate(read); gate.IsClosed = true;
        var reading = Execute(workspace, read, opened!);
        await gate.Started;
        await workspace.OpenAsync("other", CancellationToken.None);
        gate.Open();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    // Only two reads run at once, so each pending read gets the refresh to itself.
    [Fact]
    public async Task WhenARefreshRunsDuringAWorkingDiffRead_ShouldDiscardTheDiff()
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        await workspace.OpenAsync("fixture", CancellationToken.None);
        backends.Diff.Gate.IsClosed = true;
        var diff = workspace.ReadDiffAsync(Snapshots.Modified("a.txt"), false, false, CancellationToken.None);
        await backends.Diff.Gate.Started;
        await workspace.RefreshAsync(CancellationToken.None);
        backends.Diff.Gate.Open();
        Assert.Null(await diff);
    }

    // A refresh that has begun but shows nothing yet leaves the working diff read during it standing. While another
    // program keeps writing and a status read takes as long as the watcher gathers changes, the next refresh begins as
    // soon as an update is shown, right after the diff's read began; dropping the read then would drop every read.
    [Fact]
    public async Task WhenARefreshStillReadsAsAWorkingDiffReadEnds_ShouldAnswerTheDiff()
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        await workspace.OpenAsync("fixture", CancellationToken.None);
        backends.Diff.Gate.IsClosed = true;
        var diff = workspace.ReadDiffAsync(Snapshots.Modified("a.txt"), false, false, CancellationToken.None);
        await backends.Diff.Gate.Started;
        backends.Reader.Gate.IsClosed = true;
        var refresh = workspace.RefreshAsync(CancellationToken.None);
        await backends.Reader.Gate.Arrival(2);
        backends.Diff.Gate.Open();
        var answered = await diff;
        backends.Reader.Gate.Open();
        await refresh;
        Assert.NotNull(answered);
    }

    // A write that ends during a working read drops the read even when its final status read failed and left the shown
    // snapshot in place.
    [Fact]
    public async Task WhenAWriteWhoseFinalReadFailsCompletesDuringAWorkingDiffRead_ShouldAnswerNull()
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        backends.Diff.Gate.IsClosed = true;
        var diff = workspace.ReadDiffAsync(Snapshots.Modified("a.txt"), false, false, CancellationToken.None);
        await backends.Diff.Gate.Started;
        backends.Reader.Failure = new IOException("status read failed");
        var result = await workspace.WriteAsync(new StageRequest(opened, []), CancellationToken.None);
        Assert.Null(result.Snapshot);
        backends.Diff.Gate.Open();
        Assert.Null(await diff);
    }

    [Fact]
    public async Task WhenHistoryIsReadWithoutTheAllBranchesFlag_ShouldReadOnlyTheCurrentBranch()
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        Assert.NotNull(await workspace.ReadHistoryAsync(opened!, new(), CancellationToken.None));
        Assert.False(backends.History.LastAllBranches);
    }

    [Fact]
    public async Task WhenHistoryIsReadWithTheAllBranchesFlag_ShouldPassItToTheHistoryReader()
    {
        var backends = new Backends();
        await using var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        Assert.NotNull(await workspace.ReadHistoryAsync(opened!, new(AllBranches: true), CancellationToken.None));
        Assert.True(backends.History.LastAllBranches);
    }

    [Fact]
    public async Task WhenACommitIsRead_ShouldAnswerTheReadersMessageAndParents()
    {
        var (workspace, opened) = await WithACommitAsync();
        await using var _ = workspace;
        var details = await workspace.ReadCommitAsync(opened!, Snapshots.HeadId, CancellationToken.None);
        Assert.Equal("Body", details!.Message); Assert.Equal(["parent"], details.Parents);
    }

    [Fact]
    public async Task WhenACommitsFileDiffIsRead_ShouldAnswerTheReadersDiff()
    {
        var (workspace, opened) = await WithACommitAsync();
        await using var _ = workspace;
        var details = (await workspace.ReadCommitAsync(opened!, Snapshots.HeadId, CancellationToken.None))!;
        var diff = await workspace.ReadCommitDiffAsync(opened!, details, File, false, CancellationToken.None);
        Assert.Equal("+a.txt", Assert.Single(diff!.Lines).Text);
    }

    // A stage held at the writer, in flight until the test opens the writer's gate.
    private static async Task<Task<GitWriteResult>> WriteHeldAsync(Backends backends, RepositoryWorkspace workspace, RepositorySnapshot opened)
    {
        backends.Writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened, [Snapshots.Path("a")]), CancellationToken.None);
        await backends.Writer.Gate.Started;
        return write;
    }

    // A read held at its reader while a write that leaves the head as it was runs to the end; then the read is let go.
    private static async Task<(RepositoryWorkspace Workspace, RepositorySnapshot Opened, GitWriteResult Result, Task<object?> Reading)>
        WriteCompletedDuringAReadAsync(Read read)
    {
        var backends = new Backends();
        var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", default))!;
        var gate = backends.Gate(read);
        gate.IsClosed = true;
        var reading = Execute(workspace, read, opened);
        await gate.Started;

        var result = await workspace.WriteAsync(new StageRequest(opened, []), default);
        gate.Open();
        return (workspace, opened, result, reading);
    }

    // A read held at its reader and cancelled by its caller; the reader then answers anyway, as if it ignored the token.
    private static async Task<(RepositoryWorkspace Workspace, RepositorySnapshot Opened, Task<object?> Reading)> ReadCancelledByItsCallerAsync(
        Read read, CancellationTokenSource cancellation)
    {
        var backends = new Backends();
        var workspace = backends.Workspace();
        var opened = (await workspace.OpenAsync("fixture", default))!;
        var gate = backends.Gate(read);
        gate.IsClosed = true;
        var reading = Execute(workspace, read, opened, cancellation.Token);
        await gate.Started;

        cancellation.Cancel();
        gate.Open();
        return (workspace, opened, reading);
    }

    // An opened workspace whose history reader knows a commit with a body, a parent and one changed file.
    private static async Task<(RepositoryWorkspace Workspace, RepositorySnapshot? Opened)> WithACommitAsync()
    {
        var backends = new Backends();
        backends.History.Details = id => new(Snapshots.Commit(id, "Subject") with { Parents = ["parent"] }, "Body", [File]);
        var workspace = backends.Workspace();
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        return (workspace, opened);
    }
}
