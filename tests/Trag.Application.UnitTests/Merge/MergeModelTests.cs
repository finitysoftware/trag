using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Merge;

// The repository's merge over the real workspace and fake Git: when the preview of a merge to start is read, and what
// the merge in progress waits for before it finishes.
public sealed class MergeModelTests
{
    private static readonly RepositoryMerge Topic = new("incoming", Snapshots.HeadId, "Merge branch 'topic'\n\n# Conflicts:\n#\ta.txt\n", "fp", "");

    [Fact]
    public void WhenASnapshotBringsAMerge_ShouldSayItBegan()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Create(operation: RepositoryOperation.Merge, merge: Topic));
        Assert.True(repository.Merge.Began);
    }

    [Fact]
    public void WhenTheSameMergeIsSeenAgain_ShouldNotSayItBegan()
    {
        var (repository, merging) = Merging();
        repository.Apply(merging with { Files = [Snapshots.Staged("a.txt")] });
        Assert.False(repository.Merge.Began);
    }

    [Fact]
    public void WhenAnotherMergeTakesItsPlace_ShouldSayItBegan()
    {
        var (repository, merging) = Merging();
        repository.Apply(merging with { Merge = Topic with { IncomingHead = "other" } });
        Assert.True(repository.Merge.Began);
    }

    [Fact]
    public void WhenTheMergeEnds_ShouldNotSayOneBegan()
    {
        var (repository, _) = Merging();
        repository.Apply(Snapshots.Create());
        Assert.False(repository.Merge.Began);
    }

    // Choosing one branch reads nothing; the read waits for the other.
    [Fact]
    public async Task WhenOneBranchIsChosenAndThenTheOther_ShouldReadThePreviewForBoth()
    {
        var (branches, _, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var merge = repository.Merge;

        merge.ReadPreview(choice with { From = null });
        merge.ReadPreview(choice); await merge.Preview.Completion;

        Assert.Equal(("refs/heads/main", "refs/heads/feature"), Assert.Single(branches.Previews));
        Assert.True(merge.Preview.Value.IsRead);
    }

    [Fact]
    public async Task WhenThePreviewIsAskedForTheSameChoiceAgain_ShouldNotReadAgain()
    {
        var (branches, _, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var merge = repository.Merge;

        merge.ReadPreview(choice); await merge.Preview.Completion;
        merge.ReadPreview(choice); await merge.Preview.Completion;

        Assert.Equal(("refs/heads/main", "refs/heads/feature"), Assert.Single(branches.Previews));
    }

    [Fact]
    public async Task WhenTheRepositoryMoves_ShouldReadThePreviewAgain()
    {
        var (branches, reader, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var merge = repository.Merge;
        merge.ReadPreview(choice); await merge.Preview.Completion;

        await MoveTheRepositoryAsync(reader, workspace, repository);
        merge.ReadPreview(choice); await merge.Preview.Completion;

        Assert.Equal(2, branches.Previews.Count);
    }

    // A fetch moves the branch to merge while the dialog is open, and HEAD stays: the merge goes to the tip the preview
    // read, so the preview has to be the new tip's.
    [Fact]
    public async Task WhenTheFromTipMovesWhileTheDialogIsOpen_ShouldReadThePreviewAgainBeforeMerging()
    {
        var (branches, _, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var merge = repository.Merge;
        merge.ReadPreview(choice); await merge.Preview.Completion;

        merge.ReadPreview(choice with { From = choice.From! with { Head = Snapshots.Id('d') } }); await merge.Preview.Completion;

        Assert.Equal(2, branches.Previews.Count);
    }

    [Fact]
    public async Task WhenTheTipOfAnotherBranchToMergeIntoMovesWhileTheDialogIsOpen_ShouldReadThePreviewAgain()
    {
        var (branches, _, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var merge = repository.Merge;
        var intoFeature = new MergeChoice(choice.Into, choice.From, "main");
        merge.ReadPreview(intoFeature); await merge.Preview.Completion;

        merge.ReadPreview(intoFeature with { Into = intoFeature.Into! with { Head = Snapshots.Id('d') } }); await merge.Preview.Completion;

        Assert.Equal(2, branches.Previews.Count);
    }

    [Fact]
    public async Task WhenTheDialogClosesAndOpensAgain_ShouldReadThePreviewAgain()
    {
        var (branches, workspace, merge, choice) = await ReadAgainAfterTheRepositoryMovedAsync();
        await using var _ = workspace;

        merge.ClearPreview();
        merge.ReadPreview(choice); await merge.Preview.Completion;

        Assert.Equal(3, branches.Previews.Count);
    }

    [Fact]
    public async Task WhenABranchIsNoLongerChosen_ShouldShowNoPreviewAndReadNothing()
    {
        var (branches, workspace, merge, choice) = await ReadAgainAfterTheRepositoryMovedAsync();
        await using var _ = workspace;
        merge.ClearPreview();
        merge.ReadPreview(choice); await merge.Preview.Completion;

        merge.ReadPreview(choice with { Into = null });

        Assert.Same(MergePreview.None, merge.Preview.Value); Assert.Equal(3, branches.Previews.Count);
    }

    [Fact]
    public async Task WhenAMergeStopsWithAConflict_ShouldOfferGitsMessageWithoutItsConflictListAndSayConflictsAreLeft()
    {
        var (_, _, workspace, _, merge) = await OpenMergeWithAConflictAsync();
        await using var _ = workspace;
        Assert.Equal("Merge branch 'topic'", merge.Message); Assert.True(merge.Overview.HasConflicts);
    }

    [Fact]
    public async Task WhenAConflictIsLeft_ShouldNotFinish()
    {
        var (writer, _, workspace, _, merge) = await OpenMergeWithAConflictAsync();
        await using var _ = workspace;
        Assert.False(merge.CanFinish(merge.Message));
        Assert.Null(await merge.FinishAsync(merge.Message)); Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenTheMessageIsBlank_ShouldNotFinish()
    {
        var (_, reader, workspace, repository, merge) = await OpenMergeWithAConflictAsync();
        await using var _ = workspace;
        await ResolveTheConflictAsync(reader, workspace, repository);
        Assert.False(merge.CanFinish(" "));
    }

    [Fact]
    public async Task WhenEveryConflictIsResolvedAndAMessageGiven_ShouldFinishTheMergeWithThatMessage()
    {
        var (writer, reader, workspace, repository, merge) = await OpenMergeWithAConflictAsync();
        await using var _ = workspace;
        await ResolveTheConflictAsync(reader, workspace, repository);

        Assert.True(merge.CanFinish("Merged"));
        await merge.FinishAsync("Merged");

        var finish = Assert.IsType<CommitRequest>(writer.Requests[0]);
        Assert.Equal(("Merged", true), (finish.Message, finish.FinishesMerge));
    }

    // Finish ran first and fake Git leaves the merge in progress, so the abort is the second request.
    [Fact]
    public async Task WhenAMergeInProgressIsAborted_ShouldAskGitToAbortIt()
    {
        var (writer, _, workspace, _, merge) = await FinishedAsync();
        await using var _ = workspace;

        await merge.AbortAsync();

        Assert.IsType<MergeAbortRequest>(writer.Requests[1]);
    }

    [Fact]
    public async Task WhenNoMergeIsInProgress_ShouldNotAbortAndOfferNoMessage()
    {
        var (writer, reader, workspace, repository, merge) = await FinishedAsync();
        await using var _ = workspace;
        await merge.AbortAsync();
        reader.Snapshot = Snapshots.Create();
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);

        Assert.Null(await merge.AbortAsync()); Assert.Equal(2, writer.Requests.Count); Assert.Equal("", merge.Message);
    }

    // A repository that saw the merge of topic begin.
    private static (Repository Repository, RepositorySnapshot Merging) Merging()
    {
        var repository = new Repository();
        var merging = Snapshots.Create(operation: RepositoryOperation.Merge, merge: Topic);
        repository.Apply(merging);
        return (repository, merging);
    }

    // main checked out and feature beside it, opened, and the choice of merging feature into main.
    private static async Task<(FakeBranchReader Branches, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, MergeChoice Choice)> OpenWithTwoBranchesAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "feature" } };
        var reader = new FakeRepositoryReader();
        var workspace = new RepositoryWorkspace(reader, branchReader: branches);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        await repository.Branches.List.Completion;
        var (main, feature) = (repository.Branches.List.Value.Current!, repository.Branches.List.Value.Local[0]);
        return (branches, reader, workspace, repository, new MergeChoice(feature, main, "main"));
    }

    private static async Task MoveTheRepositoryAsync(FakeRepositoryReader reader, RepositoryWorkspace workspace, Repository repository)
    {
        reader.Snapshot = Snapshots.Create(head: Snapshots.Id('c'));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
    }

    // The preview read for the choice, and read again after the repository moved: two reads so far.
    private static async Task<(FakeBranchReader Branches, RepositoryWorkspace Workspace, MergeModel Merge, MergeChoice Choice)> ReadAgainAfterTheRepositoryMovedAsync()
    {
        var (branches, reader, workspace, repository, choice) = await OpenWithTwoBranchesAsync();
        var merge = repository.Merge;
        merge.ReadPreview(choice); await merge.Preview.Completion;
        await MoveTheRepositoryAsync(reader, workspace, repository);
        merge.ReadPreview(choice); await merge.Preview.Completion;
        return (branches, workspace, merge, choice);
    }

    // A merge of topic stopped with a.txt in conflict, opened.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, MergeModel Merge)> OpenMergeWithAConflictAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: Topic) };
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, reader, workspace, repository, repository.Merge);
    }

    private static async Task ResolveTheConflictAsync(FakeRepositoryReader reader, RepositoryWorkspace workspace, Repository repository)
    {
        reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")], operation: RepositoryOperation.Merge, merge: Topic);
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
    }

    // The conflict resolved and Finish asked for with "Merged": one request so far.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, MergeModel Merge)> FinishedAsync()
    {
        var opened = await OpenMergeWithAConflictAsync();
        await ResolveTheConflictAsync(opened.Reader, opened.Workspace, opened.Repository);
        await opened.Merge.FinishAsync("Merged");
        return opened;
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());
}
