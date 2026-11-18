using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Status;
using Trag.Application.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Status;

// The repository's status as the snapshot's presenter: the lists are the snapshot's before it is published, a refresh
// edits the shown lists in place, and an update the gate has moved past changes nothing.
public sealed class StatusModelTests
{
    // The events of both updates are recorded: the first replaces the lists, the refresh edits them.
    [Fact]
    public async Task WhenARefreshFollowsTheFirstSnapshot_ShouldEditTheShownListsInPlaceBeforeEachIsPublished()
    {
        var (status, events) = Recorded();

        await status.ShowAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Staged("s")]), delta: false, CancellationToken.None,
            () => events.Add("published"), SnapshotChange.Everything);
        var unstaged = status.Unstaged;
        await status.ShowAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Modified("b"), Snapshots.Staged("s")]), delta: true,
            CancellationToken.None, () => events.Add("published"), SnapshotChange.Everything);

        Assert.Equal(["replaced 1", "published", "editing", "edited 2 editing", "published", "settled"], events);
        Assert.Same(unstaged, status.Unstaged); Assert.Equal(["a", "b"], status.Unstaged.Select(file => file.Path.Display));
        Assert.Equal(["s"], status.Staged.Select(file => file.Path.Display)); Assert.False(status.IsEditing);
    }

    [Fact]
    public async Task WhenAnUpdateComesWithACanceledToken_ShouldChangeNothing()
    {
        var (status, events) = Recorded();
        await status.ShowAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Staged("s")]), delta: false, CancellationToken.None,
            () => events.Add("published"), SnapshotChange.Everything);
        await status.ShowAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Modified("b"), Snapshots.Staged("s")]), delta: true,
            CancellationToken.None, () => events.Add("published"), SnapshotChange.Everything);
        events.Clear();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => status.ShowAsync(Snapshots.Create(), delta: true, new CancellationToken(canceled: true),
            () => events.Add("published"), SnapshotChange.Everything));

        Assert.Empty(events); Assert.Equal(2, status.Unstaged.Count);
    }

    // Each side is edited in batches of its own, and Cancel comes after the first: the status bar then says the previous
    // status is shown, so the lists are that status, not part of the next one.
    [Fact]
    public async Task WhenAnUpdateIsCanceledBetweenItsBatchesOfEdits_ShouldLeaveTheListsAsTheyWere()
    {
        using var cancellation = new CancellationTokenSource();
        var status = new StatusModel(new Repository(), new CancelingDispatcher(cancellation), reader: null);
        await status.ShowAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Staged("s")]), delta: false, CancellationToken.None,
            () => { }, SnapshotChange.Everything);
        var next = Snapshots.Create(files: [Snapshots.Modified("b"), Snapshots.Staged("t")]);
        List<string> shown = [];
        status.Replaced += () => shown.AddRange(status.Staged.Concat(status.Unstaged).Select(file => file.Path.Display));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => status.ShowAsync(next, delta: true, cancellation.Token, () => { }, SnapshotChange.Everything));

        Assert.Equal(["s", "a"], shown);
    }

    // Applies each batch, then Cancel comes.
    private sealed class CancelingDispatcher(CancellationTokenSource cancellation) : IViewDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenNoRepositoryIsShownYet_ShouldOfferNoDiff()
    {
        var (_, _, _, workspace, repository) = Unopened();
        await using var _ = workspace;
        var status = repository.Status;
        Assert.Null(status.DiffOf(Snapshots.Modified("a.txt"), fromIndex: false));
    }

    // A commit needs staged files.
    [Fact]
    public async Task WhenOnlyUnstagedChangesAreShown_ShouldOfferADiscardButNoCommit()
    {
        var (_, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        Assert.False(status.CanCommit("Message")); Assert.True(status.CanDiscard);
    }

    [Fact]
    public async Task WhenAFileIsStagedOutsideAMerge_ShouldNotStageAResolution()
    {
        var (writer, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;

        await status.StageAsync([Snapshots.Modified("a.txt")]);

        Assert.False(Assert.IsType<StageRequest>(writer.LastRequest).Resolution);
    }

    // Unstaging a rename restores both of its names; staging names what git add needs, the old name only while the rename
    // is the working tree's; a name two files share goes to Git once.
    [Fact]
    public async Task WhenFilesAreStagedOrUnstaged_ShouldNameWhatGitNeedsForEachFileOnce()
    {
        var (writer, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        var indexRename = Snapshots.Renamed("old.txt", "new.txt");
        var treeRename = new FileStatus(Snapshots.Path("moved.txt"), '.', 'R', Snapshots.Path("was.txt"));

        await status.UnstageAsync([indexRename, Snapshots.Staged("new.txt")]);
        Assert.Equal(["new.txt", "old.txt"], Assert.IsType<UnstageRequest>(writer.LastRequest).Paths.Select(path => path.Display));

        await status.StageAsync([indexRename, treeRename, Snapshots.Modified("moved.txt")]);
        Assert.Equal(["new.txt", "moved.txt", "was.txt"], Assert.IsType<StageRequest>(writer.LastRequest).Paths.Select(path => path.Display));
    }

    [Fact]
    public async Task WhenTheDiffOfAnUnstagedFileIsRead_ShouldReadItOnTheUnstagedSide()
    {
        var (_, diffs, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;

        await status.DiffOf(status.Unstaged[0], fromIndex: false)!(false, CancellationToken.None);

        Assert.Equal(("a.txt", false), (diffs.Requests[^1].File.Path.Display, diffs.Requests[^1].FromIndex));
    }

    // A discard leaves a merge's files to the merge view.
    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldResolveAndOfferNeitherADiscardNorACommit()
    {
        var (_, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;

        await ShowNextAsync(reader, workspace, repository, Merging());

        Assert.True(status.IsResolving); Assert.False(status.CanDiscard); Assert.False(status.CanCommit("Message"));
    }

    [Fact]
    public async Task WhenAFileIsStagedDuringAMerge_ShouldStageAResolution()
    {
        var (writer, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        await ShowNextAsync(reader, workspace, repository, Merging());

        await status.StageAsync([Snapshots.Modified("a.txt")]);

        Assert.True(Assert.IsType<StageRequest>(writer.LastRequest).Resolution);
    }

    // A revert or a cherry-pick stopped at conflicts, and the conflicts a stash left, are resolved like a merge's: the
    // file list's Stage, which they leave enabled, stages a resolution, the only staging the writer lets through then.
    [Theory]
    [InlineData(RepositoryOperation.CherryPick)]
    [InlineData(RepositoryOperation.Revert)]
    [InlineData(RepositoryOperation.Conflict)]
    public async Task WhenAFileIsStagedDuringAPickOrTheConflictsAStashLeft_ShouldStageAResolution(RepositoryOperation operation)
    {
        var (writer, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        await ShowNextAsync(reader, workspace, repository, Snapshots.Create(files: [Snapshots.Conflict("a.txt"), Snapshots.Modified("b.txt")], operation: operation)
            with { PickHead = operation == RepositoryOperation.Conflict ? null : Snapshots.Id('c') });

        Assert.True(status.CanStage);
        await status.StageAsync([Snapshots.Modified("b.txt")]);

        Assert.True(Assert.IsType<StageRequest>(writer.LastRequest).Resolution);
    }

    [Fact]
    public async Task WhenAFileIsChangedOnTheShownSide_ShouldOfferItsHunksButNotThoseOfANewFile()
    {
        var (_, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;

        Assert.True(status.CanTakeHunk(status.Unstaged[0], fromIndex: false));
        Assert.False(status.CanTakeHunk(Snapshots.Untracked("new.txt"), fromIndex: false));
    }

    // A hunk is an ordinary write; a merge stages whole files as resolutions.
    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldNotOfferAHunk()
    {
        var (_, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        await ShowNextAsync(reader, workspace, repository, Merging());

        Assert.False(repository.Status.CanTakeHunk(Snapshots.Staged("b.txt"), fromIndex: true));
    }

    [Fact]
    public async Task WhenAHunkOfTheWorkingTreesDiffIsTaken_ShouldStageThatHunkAlone()
    {
        var (writer, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;

        await repository.Status.TakeHunkAsync(repository.Status.Unstaged[0], fromIndex: false, TwoChanges, new DiffBlock(3, 4).Lines);

        var request = Assert.IsType<PatchRequest>(writer.LastRequest);
        Assert.Equal((PatchAction.Stage, "a.txt", HunkPatch.For(TwoChanges, new DiffBlock(3, 4).Lines, Snapshots.Path("a.txt"), PatchAction.Stage), false),
            (request.Action, request.Path.Display, request.Patch, request.Lines));
    }

    // Lines that are not a whole hunk are sent as lines, so what the write says names them.
    [Fact]
    public async Task WhenChosenLinesAreLessThanTheirHunk_ShouldSendThemAsLines()
    {
        var (writer, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;

        await repository.Status.TakeHunkAsync(repository.Status.Unstaged[0], fromIndex: false, TwoChanges, [3]);

        Assert.True(Assert.IsType<PatchRequest>(writer.LastRequest).Lines);
    }

    [Fact]
    public async Task WhenAHunkOfTheIndexsDiffIsTaken_ShouldUnstageThatHunkAlone()
    {
        var (writer, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        await ShowNextAsync(reader, workspace, repository, Snapshots.Create(files: [Snapshots.Staged("a.txt")]));

        await repository.Status.TakeHunkAsync(repository.Status.Staged[0], fromIndex: true, TwoChanges, new DiffBlock(3, 4).Lines);

        var request = Assert.IsType<PatchRequest>(writer.LastRequest);
        Assert.Equal((PatchAction.Unstage, HunkPatch.For(TwoChanges, new DiffBlock(3, 4).Lines, Snapshots.Path("a.txt"), PatchAction.Unstage)), (request.Action, request.Patch));
    }

    [Fact]
    public async Task WhenAHunkOfTheWorkingTreesDiffIsDiscarded_ShouldTakeThatHunkAloneOutOfTheWorkingTree()
    {
        var (writer, _, _, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;

        await repository.Status.DiscardHunkAsync(repository.Status.Unstaged[0], TwoChanges, new DiffBlock(6, 7).Lines);

        var request = Assert.IsType<PatchRequest>(writer.LastRequest);
        Assert.Equal((PatchAction.Discard, "a.txt", HunkPatch.For(TwoChanges, new DiffBlock(6, 7).Lines, Snapshots.Path("a.txt"), PatchAction.Discard)),
            (request.Action, request.Path.Display, request.Patch));
    }

    [Fact]
    public async Task WhenTheCommitMessageIsBlank_ShouldNotCommit()
    {
        var (_, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        await ShowNextAsync(reader, workspace, repository, Snapshots.Create(files: [Snapshots.Staged("b.txt")]));

        Assert.False(status.CanCommit(" "));
    }

    [Fact]
    public async Task WhenFilesAreStagedAndAMessageIsGiven_ShouldCommitWithThatMessage()
    {
        var (writer, _, reader, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        var status = repository.Status;
        await ShowNextAsync(reader, workspace, repository, Snapshots.Create(files: [Snapshots.Staged("b.txt")]));

        Assert.True(status.CanCommit("Message"));
        await status.CommitAsync("Message");

        Assert.Equal("Message", Assert.IsType<CommitRequest>(writer.LastRequest).Message);
    }

    // A status with no repository behind it, every event recorded with the size of the unstaged list.
    private static (StatusModel Status, List<string> Events) Recorded()
    {
        var status = new StatusModel(new Repository(), new ImmediateViewDispatcher(), reader: null);
        List<string> events = [];
        status.Replaced += () => events.Add("replaced " + status.Unstaged.Count);
        status.Editing += () => events.Add("editing");
        status.Edited += () => events.Add("edited " + status.Unstaged.Count + (status.IsEditing ? " editing" : ""));
        status.Settled += _ => events.Add("settled");
        return (status, events);
    }

    // Two changes of a shown diff: d to D, and f to F.
    private static readonly PatchLine[] TwoChanges =
    [
        new(' ', " a", 1, 1), new(' ', " b", 2, 2), new(' ', " c", 3, 3), new('-', "-d", 4), new('+', "+D", null, 4), new(' ', " e", 5, 5),
        new('-', "-f", 6), new('+', "+F", null, 6), new(' ', " g", 7, 7)
    ];

    // a.txt modified, over the real workspace; nothing opened yet.
    private static (FakeGitWriter Writer, FakeFileDiffReader Diffs, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository) Unopened()
    {
        var writer = new FakeGitWriter();
        var diffs = new FakeFileDiffReader();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]) };
        var workspace = new RepositoryWorkspace(reader, diffReader: diffs, writer: writer);
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        return (writer, diffs, reader, workspace, repository);
    }

    // a.txt modified, opened and shown.
    private static async Task<(FakeGitWriter Writer, FakeFileDiffReader Diffs, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository)> OpenWithAChangeAsync()
    {
        var unopened = Unopened();
        var (workspace, repository) = (unopened.Workspace, unopened.Repository);
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        await repository.Status.ShowAsync(opened, delta: false, CancellationToken.None, () => repository.Apply(opened), SnapshotChange.Everything);
        return unopened;
    }

    // A merge stopped with a.txt in conflict and b.txt staged.
    private static RepositorySnapshot Merging() =>
        Snapshots.Create(files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")], operation: RepositoryOperation.Merge,
            merge: new("incoming", Snapshots.HeadId, "Merge", "fp", ""));

    // The refresh that reads the given snapshot, shown as a delta over the lists before it.
    private static async Task ShowNextAsync(FakeRepositoryReader reader, RepositoryWorkspace workspace, Repository repository, RepositorySnapshot snapshot)
    {
        reader.Snapshot = snapshot;
        var next = (await workspace.RefreshAsync(CancellationToken.None))!;
        await repository.Status.ShowAsync(next, delta: true, CancellationToken.None, () => repository.Apply(next), SnapshotChange.Everything);
    }
}
