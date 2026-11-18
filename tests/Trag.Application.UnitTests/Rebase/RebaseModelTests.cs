using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Rebase;
using Trag.Application.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Rebase;

// The repository's rebase over the real workspace and fake Git: what a stop waits for before it continues, and when the
// preview of a rebase to start is read.
public sealed class RebaseModelTests
{
    [Fact]
    public void WhenASnapshotBringsARebase_ShouldSayItBegan()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 1)));
        Assert.True(repository.Rebase.Began);
    }

    [Fact]
    public void WhenTheRebaseStopsAtItsNextCommit_ShouldNotSayItBegan()
    {
        var repository = AtTheFirstStop();
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 2)));
        Assert.False(repository.Rebase.Began);
    }

    [Fact]
    public void WhenAnotherRebaseTakesItsPlace_ShouldSayItBegan()
    {
        var repository = AtTheFirstStop();
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 1, branch: "other")));
        Assert.True(repository.Rebase.Began);
    }

    [Fact]
    public void WhenTheRebaseEnds_ShouldNotSayOneBegan()
    {
        var repository = AtTheFirstStop();
        repository.Apply(Snapshots.Create());
        Assert.False(repository.Rebase.Began);
    }

    [Fact]
    public async Task WhenTurningAroundSwitchesToTheOtherBranch_ShouldWaitForTheBranchesTheSwitchReadsAgain()
    {
        var (branches, writer, workspace, repository, _, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var reads = branches.Calls;
        branches.Gate.IsClosed = true;

        var turning = repository.Rebase.TurnAroundAsync(feature);
        await branches.Gate.Arrival(reads + 1); await ShellHarness.Until(() => !repository.IsWriting, "the switch to end");
        await Task.Delay(50); // A real pause: the assertion is that the turn-around has not ended meanwhile, and nothing can be awaited for that.
        Assert.False(turning.IsCompleted);
        branches.Gate.Open();
        var switched = await turning;

        Assert.Null(switched);
        var request = Assert.IsType<BranchRequest>(Assert.Single(writer.Requests));
        Assert.Equal(("feature", BranchAction.Switch), (request.Branch.Name, request.Action));
        // The switch's snapshot moved HEAD to feature, which reads the branches once; the turn-around waited for them.
        Assert.Equal(reads + 1, branches.Calls); Assert.Equal("feature", repository.Branches.List.Value.Current!.Name);
    }

    // A refused switch moves nothing, so the snapshot after it lists the same branches, and nothing reads them.
    [Fact]
    public async Task WhenGitRefusesToTurnAround_ShouldSayWhyAndReadTheBranchesNoMore()
    {
        var (branches, writer, workspace, repository, _, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        writer.Result = GitWriteResult.Rejected("Your local changes would be overwritten.");
        var reads = branches.Calls;

        Assert.Equal("Your local changes would be overwritten.", await repository.Rebase.TurnAroundAsync(feature));
        Assert.Equal(reads, branches.Calls);
    }

    [Fact]
    public async Task WhenARebaseStopsWithAConflict_ShouldOfferTheCommitsMessageAndSayConflictsAreLeft()
    {
        var (_, _, workspace, _, rebase) = await OpenStopWithAConflictAsync();
        await using var _ = workspace;
        Assert.Equal("Commit 1", rebase.Message); Assert.True(rebase.Overview.HasConflicts);
    }

    [Fact]
    public async Task WhenAConflictOfTheStopIsLeft_ShouldNotContinue()
    {
        var (writer, _, workspace, _, rebase) = await OpenStopWithAConflictAsync();
        await using var _ = workspace;
        Assert.Null(await rebase.ContinueAsync(rebase.Message)); Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenEveryConflictOfTheStopIsResolved_ShouldContinueWithTheGivenMessage()
    {
        var (writer, reader, workspace, repository, rebase) = await OpenStopWithAConflictAsync();
        await using var _ = workspace;
        await ResolveTheStopAsync(reader, workspace, repository);

        Assert.True(rebase.CanContinue("Reworded"));
        await rebase.ContinueAsync("Reworded");

        Assert.Equal("Reworded", Assert.IsType<RebaseContinueRequest>(writer.Requests[0]).Message);
    }

    // Continue ran first and fake Git leaves the rebase stopped, so the skip is the second request.
    [Fact]
    public async Task WhenTheStoppedCommitIsSkipped_ShouldAskGitToSkipIt()
    {
        var (writer, _, workspace, _, rebase) = await ContinuedAsync();
        await using var _ = workspace;

        await rebase.SkipAsync();

        Assert.IsType<RebaseSkipRequest>(writer.Requests[1]);
    }

    // Continue and Skip ran first and fake Git leaves the rebase stopped, so the abort is the third request.
    [Fact]
    public async Task WhenTheRebaseIsAborted_ShouldAskGitToAbortIt()
    {
        var (writer, _, workspace, _, rebase) = await ContinuedAsync();
        await using var _ = workspace;
        await rebase.SkipAsync();

        await rebase.AbortAsync();

        Assert.IsType<RebaseAbortRequest>(writer.Requests[2]);
    }

    [Fact]
    public async Task WhenNoRebaseIsInProgress_ShouldNotSkip()
    {
        var (writer, workspace, rebase) = await EndedAsync();
        await using var _ = workspace;
        Assert.Null(await rebase.SkipAsync()); Assert.Equal(3, writer.Requests.Count);
    }

    [Fact]
    public async Task WhenNoRebaseIsInProgress_ShouldNotAbort()
    {
        var (writer, workspace, rebase) = await EndedAsync();
        await using var _ = workspace;
        Assert.Null(await rebase.AbortAsync()); Assert.Equal(3, writer.Requests.Count);
    }

    // A choice without a branch to go onto reads nothing.
    [Fact]
    public async Task WhenTheBranchToGoOntoIsChosen_ShouldReadThePreviewForIt()
    {
        var (branches, _, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;

        rebase.ReadPreview(new(null, main));
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        Assert.Equal("refs/heads/feature", Assert.Single(branches.RebasePreviews));
    }

    [Fact]
    public async Task WhenThePreviewIsAskedForTheSameBranchAgain_ShouldNotReadAgain()
    {
        var (branches, _, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;

        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        Assert.Equal("refs/heads/feature", Assert.Single(branches.RebasePreviews));
    }

    // A fetch moves the branch to go onto while the dialog is open, and HEAD stays: the rebase goes onto the tip the
    // preview read, so the preview has to be the new tip's.
    [Fact]
    public async Task WhenTheOntoTipMovesWhileTheDialogIsOpen_ShouldReadThePreviewAgain()
    {
        var (branches, _, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        rebase.ReadPreview(new(feature with { Head = Snapshots.Id('d') }, main)); await rebase.Preview.Completion;

        Assert.Equal(2, branches.RebasePreviews.Count);
    }

    [Fact]
    public async Task WhenTheDialogCloses_ShouldClearThePreview()
    {
        var (_, _, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        rebase.ClearPreview(); Assert.Same(RebasePreview.None, rebase.Preview.Value);
    }

    [Fact]
    public async Task WhenTheDialogOpensAgain_ShouldReadThePreviewAgain()
    {
        var (branches, _, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;
        rebase.ClearPreview();

        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        Assert.Equal(2, branches.RebasePreviews.Count);
    }

    [Fact]
    public async Task WhenTheRebaseStarts_ShouldGoOntoTheCommitThePreviewWasReadFor()
    {
        var (_, writer, workspace, repository, main, feature) = await OpenWithTwoBranchesAsync();
        await using var _ = workspace;
        var rebase = repository.Rebase;
        rebase.ReadPreview(new(feature, main)); await rebase.Preview.Completion;

        await rebase.StartAsync(feature, rebase.Preview.Value.OntoHead);

        var request = Assert.IsType<RebaseRequest>(Assert.Single(writer.Requests));
        Assert.Equal(("feature", Snapshots.Id('b')), (request.OntoName, request.OntoHead));
    }

    // A repository that saw a rebase begin, stopped at its first commit.
    private static Repository AtTheFirstStop()
    {
        var repository = new Repository();
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 1)));
        return repository;
    }

    // main checked out and feature beside it, opened, with the branches read.
    private static async Task<(FakeBranchReader Branches, FakeGitWriter Writer, RepositoryWorkspace Workspace, Repository Repository, BranchInfo Main, BranchInfo Feature)> OpenWithTwoBranchesAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "feature" } };
        var reader = new FakeRepositoryReader();
        var writer = new FakeGitWriter();
        // A switch Git accepts moves HEAD to the branch, in the snapshot and in the list, as Git's does.
        writer.Handle = (request, _) =>
        {
            if (writer.Result.Outcome == GitWriteOutcome.Succeeded && request is BranchRequest { Action: BranchAction.Switch } switching)
            {
                reader.Snapshot = reader.Snapshot with { Branch = switching.Branch.Name };
                branches.Current = switching.Branch.Name;
            }
            return Task.FromResult(writer.Result);
        };
        var workspace = new RepositoryWorkspace(reader, writer: writer, branchReader: branches);
        var repository = new Repository(Services(workspace));
        // A write's verified snapshot reaches the repository, as the session's does.
        repository.Writer.ApplySnapshotAsync = snapshot => { repository.Apply(snapshot); return Task.CompletedTask; };
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Branches.List.Completion;
        var (main, feature) = (repository.Branches.List.Value.Current!, repository.Branches.List.Value.Local.Single(branch => branch.Name == "feature"));
        return (branches, writer, workspace, repository, main, feature);
    }

    // A rebase stopped at its first commit with f.cs in conflict, one block left, opened.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, RebaseModel Rebase)> OpenStopWithAConflictAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader
        {
            Snapshot = Snapshots.Rebasing(Snapshots.Rebase(step: 1), [Snapshots.Conflict("f.cs")]) with { Resolutions = new([], [new(Snapshots.Path("f.cs"), 1)]) }
        };
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, reader, workspace, repository, repository.Rebase);
    }

    private static async Task ResolveTheStopAsync(FakeRepositoryReader reader, RepositoryWorkspace workspace, Repository repository)
    {
        reader.Snapshot = Snapshots.Rebasing(Snapshots.Rebase(step: 1), [Snapshots.Staged("f.cs")]) with { Resolutions = new([new(Snapshots.Path("f.cs"), Resolution.Theirs)], []) };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
    }

    // The stop resolved and Continue asked for with "Reworded": one request so far.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, RebaseModel Rebase)> ContinuedAsync()
    {
        var opened = await OpenStopWithAConflictAsync();
        await ResolveTheStopAsync(opened.Reader, opened.Workspace, opened.Repository);
        await opened.Rebase.ContinueAsync("Reworded");
        return opened;
    }

    // Continue, Skip and Abort asked for, then the rebase ended: three requests so far.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, RebaseModel Rebase)> EndedAsync()
    {
        var (writer, reader, workspace, repository, rebase) = await ContinuedAsync();
        await rebase.SkipAsync();
        await rebase.AbortAsync();
        reader.Snapshot = Snapshots.Create();
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        return (writer, workspace, rebase);
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());
}
