using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Merge;

// The repository's conflicts over the real workspace and fake Git: what is worked out per snapshot, when the shown
// conflict is read, and what each write asks Git for.
public sealed class ConflictsModelTests
{
    private static RepositorySnapshot Merging(MergeResolutions resolutions, params FileStatus[] files) =>
        Snapshots.Create(files: files, operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""))
            with { Resolutions = resolutions };

    [Fact]
    public async Task WhenASnapshotArrives_ShouldWorkOutTheFilesAndWhereTheyAreResolvedBeforeAnyScreenFollowsIt()
    {
        var reader = new FakeRepositoryReader { Snapshot = Merging(MergeResolutions.None, Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")) };
        await using var workspace = new RepositoryWorkspace(reader, diffReader: new FakeFileDiffReader());
        var repository = new Repository(Services(workspace));
        (MergeFiles Files, WorkspaceSection? Section)? seen = null;
        repository.SnapshotApplied += (_, _) => seen = (repository.Conflicts.Files, repository.Conflicts.Scope.Section);

        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);

        Assert.Equal(["a.txt"], seen!.Value.Files.Conflicts.Select(conflict => conflict.File.Path.Display));
        Assert.Equal(["b.txt"], seen.Value.Files.Resolved.Select(file => file.Path.Display));
        Assert.Equal(WorkspaceSection.Merge, seen.Value.Section);
    }

    [Fact]
    public async Task WhenTheRepositoryOpens_ShouldReadNoConflictBeforeOneIsShown()
    {
        var (diffs, _, workspace, _, _) = await OpenTwoConflictsAsync();
        await using var _ = workspace;
        Assert.Empty(diffs.ConflictReads);
    }

    [Fact]
    public async Task WhenAConflictIsShown_ShouldReadIt()
    {
        var (diffs, _, workspace, _, conflicts) = await OpenTwoConflictsAsync();
        await using var _ = workspace;

        conflicts.Show(conflicts.Files.Conflicts[0]); await conflicts.Document.Completion;

        Assert.Single(diffs.ConflictReads); Assert.Equal("hash", conflicts.Document.Value.Hash);
    }

    // The snapshot read the shown file once, so a screen that shows the same file again after it asks for nothing more.
    [Fact]
    public async Task WhenASnapshotArrivesWhileAConflictIsShown_ShouldReadItOnceMoreAndShowItsNewRow()
    {
        var (diffs, reader, workspace, repository, conflicts) = await OpenTwoConflictsAsync();
        await using var _ = workspace;
        conflicts.Show(conflicts.Files.Conflicts[0]); await conflicts.Document.Completion;

        await TakeTheBlockInTheEditorAsync(reader, workspace, repository);
        conflicts.Show(conflicts.Files.Conflicts[0]); await conflicts.Document.Completion;

        Assert.Equal(2, diffs.ConflictReads.Count); Assert.Equal(0, conflicts.Shown!.Blocks);
    }

    [Fact]
    public async Task WhenAnotherConflictIsShown_ShouldReadIt()
    {
        var (diffs, _, workspace, _, conflicts) = await ShowingTheFirstAgainAfterAnEditAsync();
        await using var _ = workspace;

        conflicts.Show(conflicts.Files.Conflicts[1]); await conflicts.Document.Completion;

        Assert.Equal(["a.txt", "a.txt", "b.txt"], diffs.ConflictReads.Select(file => file.Path.Display));
    }

    [Fact]
    public async Task WhenTheShownConflictIsResolved_ShouldShowItNoMoreAndReadNothing()
    {
        var (diffs, reader, workspace, repository, conflicts) = await ShowingTheFirstAgainAfterAnEditAsync();
        await using var _ = workspace;
        conflicts.Show(conflicts.Files.Conflicts[1]); await conflicts.Document.Completion;

        reader.Snapshot = Merging(MergeResolutions.None, Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt"));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);

        Assert.Null(conflicts.Shown); Assert.Same(ConflictView.None, conflicts.Document.Value); Assert.Equal(3, diffs.ConflictReads.Count);
    }

    [Fact]
    public async Task WhenAConflictedFileCannotBeRead_ShouldSaySoWhereItsBlocksWouldShow()
    {
        var diffs = new FakeFileDiffReader { Conflict = _ => throw new IOException("locked") };
        var reader = new FakeRepositoryReader { Snapshot = Merging(new([], [new(Snapshots.Path("a.txt"), 1)]), Snapshots.Conflict("a.txt")) };
        await using var workspace = new RepositoryWorkspace(reader, diffReader: diffs);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        repository.Conflicts.Show(repository.Conflicts.Files.Conflicts[0]); await repository.Conflicts.Document.Completion;
        Assert.Equal("The conflicted file could not be read. Refresh and try again.", repository.Conflicts.Document.Message);
    }

    [Fact]
    public async Task WhenAConflictStillHasABlock_ShouldNotMarkItResolved()
    {
        var (writer, _, workspace, _, conflicts) = await OpenMergeAsync();
        await using var _ = workspace;

        await conflicts.StageAsync(conflicts.Files.Conflicts[0]);

        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenAFileGitMergedOnItsOwnIsReopened_ShouldNotBringItBackAsAConflict()
    {
        var (writer, _, workspace, _, conflicts) = await OpenMergeAsync();
        await using var _ = workspace;

        await conflicts.ReopenAsync(conflicts.Files.Resolved[0]);

        Assert.Empty(writer.Requests);
    }

    // A block is taken in the file as it was read, and with no conflict shown none was read.
    [Fact]
    public async Task WhenABlockIsTakenWithNoConflictShown_ShouldWriteNothing()
    {
        var (writer, _, workspace, _, conflicts) = await OpenMergeAsync();
        await using var _ = workspace;

        Assert.Null(await conflicts.TakeAsync(new([], false, Block: 0), ConflictChoice.Theirs));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenYoursIsUsedInAMerge_ShouldAskGitForOurs()
    {
        var (writer, _, workspace, _, conflicts) = await OpenMergeAsync();
        await using var _ = workspace;
        var conflict = conflicts.Files.Conflicts[0];

        await conflicts.UseAsync(conflict.File.Path, conflicts.Sides.Yours);

        Assert.Equal(ConflictChoice.Mine, Assert.IsType<ConflictSideRequest>(writer.Requests[0]).Side);
    }

    // The take carries the hash the shown file was read with. Yours is written first, so the take is the second request.
    [Fact]
    public async Task WhenABlockOfTheShownConflictIsTaken_ShouldAskGitForItInTheFileAsItWasRead()
    {
        var (writer, _, workspace, _, conflicts) = await OpenMergeAsync();
        await using var _ = workspace;
        var conflict = conflicts.Files.Conflicts[0];
        await conflicts.UseAsync(conflict.File.Path, conflicts.Sides.Yours);

        conflicts.Show(conflict); await conflicts.Document.Completion;
        await conflicts.TakeAsync(conflicts.Document.Value.Parts.Single(part => part.IsBlock), ConflictChoice.Both);

        var take = Assert.IsType<ConflictBlockRequest>(writer.Requests[1]);
        Assert.Equal(("a.txt", 0, ConflictChoice.Both, "hash"), (take.Path.Display, take.Block, take.Choice, take.ContentHash));
    }

    [Fact]
    public async Task WhenYoursIsUsedInARebase_ShouldAskGitForTheirs()
    {
        var (writer, workspace, conflicts) = await RebasingAfterTheMergeWritesAsync();
        await using var _ = workspace;

        await conflicts.UseAsync(conflicts.Files.Conflicts[0].File.Path, conflicts.Sides.Yours);

        Assert.Equal(ConflictChoice.Theirs, Assert.IsType<ConflictSideRequest>(writer.Requests[2]).Side);
    }

    // Yours is written first, so marking resolved is the fourth request.
    [Fact]
    public async Task WhenAConflictHasNoBlockLeft_ShouldMarkItResolved()
    {
        var (writer, workspace, conflicts) = await RebasingAfterTheMergeWritesAsync();
        await using var _ = workspace;
        await conflicts.UseAsync(conflicts.Files.Conflicts[0].File.Path, conflicts.Sides.Yours);

        await conflicts.StageAsync(conflicts.Files.Conflicts[0]);

        Assert.True(Assert.IsType<StageRequest>(writer.Requests[3]).Resolution);
    }

    // Bringing it back as a conflict would lose the edit.
    [Fact]
    public async Task WhenAFileEditedByHandIsAskedBack_ShouldWaitForTheUser()
    {
        var (writer, workspace, conflicts) = await OpenTwoDecidedFilesAsync();
        await using var _ = workspace;
        MergeResolved File(string path) => conflicts.Files.Resolved.Single(file => file.Path.Display == path);

        Assert.True(await conflicts.RequestReopenAsync(File("edited.txt")));
        Assert.Empty(writer.Requests);
    }

    // A repository that cannot resolve now, as one whose last write waits for review, brings nothing back and asks nothing.
    [Fact]
    public async Task WhenNoResolutionIsPossibleNow_ShouldNeitherAskNorBringAFileBack()
    {
        var (writer, workspace, conflicts) = await OpenTwoDecidedFilesAsync();
        await using var _ = workspace;
        MergeResolved File(string path) => conflicts.Files.Resolved.Single(file => file.Path.Display == path);
        conflicts.Repository.NeedsReview = true;

        Assert.False(await conflicts.RequestReopenAsync(File("edited.txt")));
        Assert.False(await conflicts.RequestReopenAsync(File("mine.txt")));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenAFileDecidedByASideIsAskedBack_ShouldBringItBackAtOnce()
    {
        var (writer, workspace, conflicts) = await OpenTwoDecidedFilesAsync();
        await using var _ = workspace;
        MergeResolved File(string path) => conflicts.Files.Resolved.Single(file => file.Path.Display == path);

        Assert.False(await conflicts.RequestReopenAsync(File("mine.txt")));
        Assert.Equal("mine.txt", Assert.IsType<ReopenConflictRequest>(Assert.Single(writer.Requests)).Path.Display);
    }

    // A merge with two conflicts, a.txt with one block left, opened.
    // A stash applied over changed files left the conflict: yours is the stash, which Git calls theirs.
    [Fact]
    public async Task WhenYoursIsUsedOnConflictsAStashLeft_ShouldAskGitForTheStashSide()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(operation: RepositoryOperation.Conflict, files: [Snapshots.Conflict("a.txt")]) };
        await using var workspace = new RepositoryWorkspace(reader, diffReader: new FakeFileDiffReader(), writer: writer);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);

        await repository.Conflicts.UseAsync(Snapshots.Path("a.txt"), repository.Conflicts.Sides.Yours);

        Assert.Equal(ConflictChoice.Theirs, Assert.IsType<ConflictSideRequest>(Assert.Single(writer.Requests)).Side);
    }

    private static async Task<(FakeFileDiffReader Diffs, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, ConflictsModel Conflicts)> OpenTwoConflictsAsync()
    {
        var diffs = new FakeFileDiffReader();
        var reader = new FakeRepositoryReader { Snapshot = Merging(new([], [new(Snapshots.Path("a.txt"), 1)]), Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt")) };
        var workspace = new RepositoryWorkspace(reader, diffReader: diffs);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (diffs, reader, workspace, repository, repository.Conflicts);
    }

    // The block was taken in the editor: the new row is the same file.
    private static async Task TakeTheBlockInTheEditorAsync(FakeRepositoryReader reader, RepositoryWorkspace workspace, Repository repository)
    {
        reader.Snapshot = Merging(new([], [new(Snapshots.Path("a.txt"), 0)]), Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt"));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
    }

    // a.txt shown, its block taken in the editor, and shown again after that snapshot: a.txt was read twice.
    private static async Task<(FakeFileDiffReader Diffs, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, ConflictsModel Conflicts)> ShowingTheFirstAgainAfterAnEditAsync()
    {
        var opened = await OpenTwoConflictsAsync();
        var conflicts = opened.Conflicts;
        conflicts.Show(conflicts.Files.Conflicts[0]); await conflicts.Document.Completion;
        await TakeTheBlockInTheEditorAsync(opened.Reader, opened.Workspace, opened.Repository);
        conflicts.Show(conflicts.Files.Conflicts[0]); await conflicts.Document.Completion;
        return opened;
    }

    // A merge stopped at a.txt with one block left, and auto.txt, which Git merged on its own.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, ConflictsModel Conflicts)> OpenMergeAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader
        {
            Snapshot = Merging(new([], [new(Snapshots.Path("a.txt"), 1)]), Snapshots.Conflict("a.txt"), Snapshots.Staged("auto.txt"))
        };
        var workspace = new RepositoryWorkspace(reader, diffReader: new FakeFileDiffReader(), writer: writer);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, reader, workspace, repository, repository.Conflicts);
    }

    // The merge's two writes (yours for a.txt, then both for its block), then a rebase stopped at a.txt with no block left.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, ConflictsModel Conflicts)> RebasingAfterTheMergeWritesAsync()
    {
        var (writer, reader, workspace, repository, conflicts) = await OpenMergeAsync();
        var conflict = conflicts.Files.Conflicts[0];
        await conflicts.UseAsync(conflict.File.Path, conflicts.Sides.Yours);
        conflicts.Show(conflict); await conflicts.Document.Completion;
        await conflicts.TakeAsync(conflicts.Document.Value.Parts.Single(part => part.IsBlock), ConflictChoice.Both);
        reader.Snapshot = Snapshots.Rebasing(Snapshots.Rebase(), [Snapshots.Conflict("a.txt")]) with { Resolutions = new([], [new(Snapshots.Path("a.txt"), 0)]) };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        return (writer, workspace, conflicts);
    }

    // A merge with mine.txt decided by a side and edited.txt edited by hand.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, ConflictsModel Conflicts)> OpenTwoDecidedFilesAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader
        {
            Snapshot = Merging(new([new(Snapshots.Path("mine.txt"), Resolution.Mine), new(Snapshots.Path("edited.txt"), Resolution.Edited)], []),
                Snapshots.Staged("edited.txt"))
        };
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, workspace, repository.Conflicts);
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());
}
