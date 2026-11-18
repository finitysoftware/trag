using Trag.Application.Branches;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Branches;

// The repository's branches over the real workspace and fake Git: when the list reads, and what each write asks Git for.
public sealed class BranchesModelTests
{
    [Fact]
    public async Task WhenTheRepositoryOpens_ShouldReadTheBranches()
    {
        var (branches, _, workspace, repository) = await OpenWithARefusingGitAsync();
        await using var _ = workspace;

        Assert.Equal(1, branches.Calls);
        Assert.Equal("main", repository.Branches.List.Value.Current!.Name); Assert.Equal(["topic"], repository.Branches.List.Value.Local.Select(branch => branch.Name));
    }

    // The list follows the checked-out branch and the local and remote-tracking refs: a snapshot that moved none of
    // them, however much else it moved, has nothing new to list.
    [Fact]
    public async Task WhenASnapshotMovedNoRef_ShouldNotReadTheBranchesAgain()
    {
        var (branches, reader, workspace, repository) = await OpenWithARefusingGitAsync();
        await using var _ = workspace;

        reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")], indexFingerprint: "staged");
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Branches.List.Completion;

        Assert.Equal(1, branches.Calls);
    }

    // A commit, a branch created or deleted, a fetch and a switch, by Trag or by any other program, each move one of
    // them; the snapshot that shows the move reads the list again.
    [Fact]
    public async Task WhenASnapshotMovedARef_ShouldReadTheBranchesAgain()
    {
        var (branches, reader, workspace, repository) = await OpenWithARefusingGitAsync();
        await using var _ = workspace;
        branches.Local.Add("fixed");

        foreach (var moved in new[] { Snapshots.Create(head: Snapshots.Id('b')) with { LocalRefs = "committed" }, Snapshots.Create() with { RemoteRefs = "fetched" }, Snapshots.Create(branch: "topic") })
        {
            reader.Snapshot = moved;
            repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Branches.List.Completion;
        }

        Assert.Equal(4, branches.Calls); Assert.Contains("fixed", repository.Branches.List.Value.LocalNames);
    }

    // A refused switch moved nothing, so the snapshot after it lists the same branches: the list stays, without a read.
    [Fact]
    public async Task WhenGitRefusesASwitch_ShouldKeepTheBranchesWithoutReadingThemAgain()
    {
        var (branches, _, workspace, repository) = await OpenWithARefusingGitAsync();
        await using var _ = workspace;

        await repository.Branches.SwitchToAsync(repository.Branches.List.Value.Local[0]); await repository.Branches.List.Completion;

        Assert.Equal(1, branches.Calls); Assert.Equal("main", repository.Branches.List.Value.Current!.Name);
    }

    [Fact]
    public async Task WhenARemoteBranchsCounterpartIsCheckedOut_ShouldMoveNowhere()
    {
        var (_, workspace, repository, remote) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;
        var list = repository.Branches.List.Value;
        Assert.Null(BranchMoves.SwitchTo(remote["origin/main"], list));
    }

    [Fact]
    public async Task WhenARemoteBranchHasALocalCounterpart_ShouldSwitchToTheCounterpart()
    {
        var (_, workspace, repository, remote) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;
        var list = repository.Branches.List.Value;
        Assert.Equal(new BranchMove(BranchAction.Switch, new("topic")), BranchMoves.SwitchTo(remote["origin/topic"], list));
    }

    [Fact]
    public async Task WhenARemoteBranchHasNoLocalCounterpart_ShouldCreateOne()
    {
        var (_, workspace, repository, remote) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;
        var list = repository.Branches.List.Value;
        Assert.Equal(BranchAction.Create, BranchMoves.SwitchTo(remote["origin/new"], list)!.Action);
    }

    // A new branch from a remote one checks out files whose Git LFS content may be only on the remote, so it comes down
    // first, with the remote's credentials; a download that fails leaves the switch unsent. A local counterpart is a
    // branch here, switched to without the remote.
    [Fact]
    public async Task WhenARemoteBranchIsSwitchedTo_ShouldDownloadTheLfsContentOfANewBranchFirstAndSwitchOnlyAfterIt()
    {
        var (writer, workspace, repository, remote) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;
        writer.Result = new(GitWriteOutcome.Rejected, "not downloaded");

        var switched = await repository.Branches.SwitchToAsync(remote["origin/new"]);

        var download = Assert.IsType<LfsDownloadRequest>(Assert.Single(writer.Requests));
        Assert.Equal(("origin", remote["origin/new"].Head, "not downloaded"), (download.Remote, download.Commit, switched!.Message));
        writer.Result = new(GitWriteOutcome.Succeeded, "");
        await repository.Branches.SwitchToAsync(remote["origin/new"]);
        Assert.Equal(BranchAction.Create, Assert.IsType<BranchRequest>(writer.Requests[^1]).Action);
        writer.Requests.Clear();
        await repository.Branches.SwitchToAsync(remote["origin/topic"]);
        Assert.Equal(BranchAction.Switch, Assert.IsType<BranchRequest>(Assert.Single(writer.Requests)).Action);
    }

    [Fact]
    public async Task WhenTheUserConfirmsDeletingALocalBranch_ShouldForceItWhereTheFirstTryDidNot()
    {
        var (writer, workspace, repository, _) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;
        var topic = repository.Branches.List.Value.Local.Single(branch => branch.Name == "topic");
        await repository.Branches.DeleteAsync(topic, confirmed: false);

        await repository.Branches.DeleteAsync(topic, confirmed: true);

        var local = writer.Requests.OfType<BranchRequest>().ToArray();
        Assert.Equal([false, true], local.Select(request => request.Branch.ForceAt is not null));
    }

    [Fact]
    public async Task WhenTheUserConfirmsDeletingARemoteBranch_ShouldDeleteItOnItsRemote()
    {
        var (writer, workspace, repository, remote) = await OpenWithRemoteBranchesAsync();
        await using var _ = workspace;

        await repository.Branches.DeleteAsync(remote["origin/topic"], confirmed: true);

        Assert.Equal("origin", Assert.IsType<RemoteBranchDeleteRequest>(writer.Requests[^1]).Remote);
    }

    [Fact]
    public async Task WhenAMoveWouldLeaveTheCommitsOfADetachedHeadBehind_ShouldWaitForTheUsersDecision()
    {
        var (writer, _, workspace, repository) = await OpenDetachedWithACommitOfItsOwnAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        var asked = 0;
        branches.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BranchesModel.Asking)) asked++; };
        Task<GitWriteResult?> Switch() => branches.WriteAsync(BranchAction.Switch, new("main"));

        Assert.Null(await branches.LeaveDetachedThenAsync(Switch));
        Assert.Empty(writer.Requests); Assert.Equal((Snapshots.Id('b'), 1), (branches.Asking, asked));
        Assert.Equal("kept-from-bbbbbbb", branches.KeptName);
    }

    // Windows keeps a branch as a file whose name ignores case, so a kept branch in another case takes the name too.
    [Fact]
    public async Task WhenTheKeptBranchNameIsTakenInAnotherCase_ShouldSuggestTheNextNumber()
    {
        var (writer, _, workspace, repository) = await OpenDetachedWithACommitOfItsOwnAsync("Kept-From-bbbbbbb");
        await using var _ = workspace;
        var branches = repository.Branches;

        Assert.Null(await branches.LeaveDetachedThenAsync(SwitchToMain(branches)));

        Assert.Equal("kept-from-bbbbbbb-2", branches.KeptName);
        Assert.Empty(writer.Requests);
    }

    // Keeping creates the branch at HEAD without switching, so the commits are reachable before the move runs.
    [Fact]
    public async Task WhenTheUserKeepsTheCommits_ShouldCreateABranchAtHeadAndThenMove()
    {
        var (writer, _, workspace, repository) = await OpenDetachedWithACommitOfItsOwnAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        var asked = 0;
        branches.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BranchesModel.Asking)) asked++; };
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        Assert.Null(await branches.KeepThenLeaveAsync("kept"));
        Assert.Equal([BranchAction.Create, BranchAction.Switch], writer.Requests.Cast<BranchRequest>().Select(request => request.Action));
        var kept = ((BranchRequest)writer.Requests[0]).Branch;
        Assert.Equal(("kept", null, false), (kept.Name, kept.StartPoint, kept.Switch)); Assert.Equal((null, 2), (branches.Asking, asked));
    }

    // Keeping and leaving are two writes: when Git refuses the move after the branch was made, the branch stays and keeps
    // the commits, the question is answered, and the status bar says both: where the commits are kept, and why the move
    // did not run.
    [Fact]
    public async Task WhenKeepingDetachedCommitsAndTheSwitchIsRefused_ShouldLeaveTheKeptBranchAndSayBoth()
    {
        var statusBar = new StatusBarState();
        var writer = new FakeGitWriter();
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: null, head: Snapshots.Id('b'), detachedCommits: 1) },
            writer: writer, branchReader: new FakeBranchReader { Local = { "main" } });
        await using var _ = workspace;
        var repository = new Repository(new RepositoryServices(workspace, statusBar, new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        writer.Handle = (request, _) => Task.FromResult(request is BranchRequest { Action: BranchAction.Switch }
            ? GitWriteResult.Rejected("Your local changes would be overwritten by the switch.") : new GitWriteResult(GitWriteOutcome.Succeeded, "Created kept."));
        var branches = repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        var answer = await branches.KeepThenLeaveAsync("kept");

        Assert.Null(answer);
        Assert.Equal([BranchAction.Create, BranchAction.Switch], writer.Requests.Cast<BranchRequest>().Select(request => request.Action));
        Assert.Null(branches.Asking);
        Assert.Equal("Your local changes would be overwritten by the switch.", repository.Writer.LastWrite!.Result!.Message);
        Assert.Equal("The commits are kept on kept. Your local changes would be overwritten by the switch.", statusBar.Status);
    }

    // The decision belongs to the HEAD that asked for it.
    [Fact]
    public async Task WhenHeadMovedBeforeTheUserDecides_ShouldRunNothingAndKeepTheMoveWaiting()
    {
        var (writer, reader, workspace, repository) = await KeptOnceAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        reader.Snapshot = reader.Snapshot with { Head = Snapshots.Id('c') };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);

        Assert.Equal(BranchText.ChooseAgain, await branches.LeaveBehindAsync()); Assert.Equal(2, writer.Requests.Count); Assert.NotNull(branches.Asking);
    }

    [Fact]
    public async Task WhenTheUserStaysDetached_ShouldStopAsking()
    {
        var (_, _, workspace, repository) = await OpenDetachedWithACommitOfItsOwnAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        branches.StayDetached(); Assert.Null(branches.Asking);
    }

    [Fact]
    public async Task WhenTheUserLeavesTheCommitsBehind_ShouldOnlyMove()
    {
        var (writer, _, workspace, repository) = await KeptOnceAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        Assert.Null(await branches.LeaveBehindAsync()); Assert.Equal(BranchAction.Switch, ((BranchRequest)writer.Requests[^1]).Action); Assert.Equal(3, writer.Requests.Count);
    }

    [Fact]
    public async Task WhenHeadIsOnABranch_ShouldMoveAtOnce()
    {
        var (writer, reader, workspace, repository) = await KeptOnceAsync();
        await using var _ = workspace;
        var branches = repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));
        await branches.LeaveBehindAsync();
        reader.Snapshot = Snapshots.Create();
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);

        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));

        Assert.Equal(4, writer.Requests.Count); Assert.Null(branches.Asking);
    }

    [Fact]
    public async Task WhenGoingToTheCommitAtHead_ShouldSaySoAndReadNothing()
    {
        var (branches, _, _, workspace, repository, services) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        var reads = branches.Calls;
        Assert.Null(await repository.Branches.GoToAsync(Snapshots.Commit(Snapshots.HeadId, "Here")));
        Assert.Equal((BranchText.AlreadyOnCommit, reads), (services.StatusBar.Status, branches.Calls));
    }

    // Switching to another branch is a decision, not a side effect of a double-click.
    [Fact]
    public async Task WhenGoingToABranchsTip_ShouldAskToSwitchThereWithoutWritingAnything()
    {
        var (_, writer, _, workspace, repository, _) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        var tip = await repository.Branches.GoToAsync(Snapshots.Commit(Snapshots.Id('b'), "Topic's tip"));
        Assert.Equal("topic", tip!.Tip!.Name);
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenGoingToAnyOtherCommit_ShouldAskWithASuggestedName()
    {
        var (_, _, _, workspace, repository, _) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        var commit = Snapshots.Commit(Snapshots.Id('c'), "Fix the login");
        var question = await repository.Branches.GoToAsync(commit);
        Assert.Null(question!.Tip);
        Assert.Equal(BranchNames.SuggestFor(commit.Subject, commit.Id, repository.Branches.List.Value.LocalNames), question.SuggestedName);
    }

    // A HEAD detached at the tip of a branch is on that commit but not on the branch: the double-click offers the switch
    // there. Detached where no branch ends, there is still nothing to do.
    [Fact]
    public async Task WhenDetachedAtATipAndTheTipIsDoubleClicked_ShouldOfferTheSwitch()
    {
        var (_, writer, reader, workspace, repository, services) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        reader.Snapshot = Snapshots.Create(branch: null, head: Snapshots.Id('b'));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);

        var question = await repository.Branches.GoToAsync(Snapshots.Commit(Snapshots.Id('b'), "Topic's tip"));

        Assert.Equal("topic", question!.Tip!.Name);
        Assert.Empty(writer.Requests);
        reader.Snapshot = Snapshots.Create(branch: null, head: Snapshots.Id('c'));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        Assert.Null(await repository.Branches.GoToAsync(Snapshots.Commit(Snapshots.Id('c'), "Detached here")));
        Assert.Equal(BranchText.AlreadyOnCommit, services.StatusBar.Status);
    }

    // A refresh that changes neither HEAD nor a ref, as a saved file's does, puts another snapshot in place while the
    // branches are read: the question still opens. One that moved HEAD or a branch, whose tip the list may no longer show,
    // asks nothing and says why.
    [Fact]
    public async Task WhenTheSnapshotIsReplacedDuringTheReload_ShouldStillGoOrSayWhy()
    {
        var (branches, _, reader, workspace, repository, services) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        var commit = Snapshots.Commit(Snapshots.Id('b'), "Topic's tip");

        var going = GoWhileRefreshing(reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] });
        Assert.Equal("topic", (await going)!.Tip!.Name);

        foreach (var moved in new[] { reader.Snapshot with { LocalRefs = "topic moved" }, reader.Snapshot with { Head = Snapshots.Id('d') } })
        {
            services.StatusBar.Status = "";
            Assert.Null(await GoWhileRefreshing(moved));
            Assert.Equal(BranchText.MovedWhileReading, services.StatusBar.Status);
        }

        async Task<GoToQuestion?> GoWhileRefreshing(RepositorySnapshot next)
        {
            var arrival = branches.Gate.Entries + 1;
            branches.Gate.IsClosed = true;
            var asking = repository.Branches.GoToAsync(commit);
            await branches.Gate.Arrival(arrival);
            reader.Snapshot = next;
            repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
            branches.Gate.Open();
            return await asking;
        }
    }

    [Fact]
    public async Task WhenTheRepositoryMovesWhileTheBranchesAreRead_ShouldAskNothing()
    {
        var (branches, _, reader, workspace, repository, _) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        var commit = Snapshots.Commit(Snapshots.Id('c'), "Fix the login");

        branches.Gate.IsClosed = true;
        var going = repository.Branches.GoToAsync(commit);
        await branches.Gate.Started;
        reader.Snapshot = Snapshots.Create(head: Snapshots.Id('d'));
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        branches.Gate.Open();

        Assert.Null(await going);
    }

    [Fact]
    public async Task WhenSwitchingToARemoteBranchWhoseCounterpartIsCheckedOut_ShouldSaySoInsteadOfWriting()
    {
        var (_, writer, _, workspace, repository, services) = await OpenWithATopicTipAsync();
        await using var _ = workspace;
        await repository.Branches.List.Completion;

        Assert.Null(await repository.Branches.SwitchToAsync(repository.Branches.List.Value.Remote.Single()));
        Assert.Equal(BranchText.AlreadyOn("main"), services.StatusBar.Status);
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenAnotherOperationIsInProgress_ShouldKeepHistoryReadOnly()
    {
        var (branches, workspace, repository) = await OpenDuringAsync(Snapshots.Create(operation: RepositoryOperation.CherryPick));
        await using var _ = workspace;
        var reads = branches.Calls;
        var commit = Snapshots.Commit(Snapshots.Id('c'), "Elsewhere");
        Assert.False(repository.Branches.CanGoTo);
        Assert.Null(await repository.Branches.GoToAsync(commit));
        Assert.Equal(reads, branches.Calls);
    }

    // The question shows what blocks the move.
    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldStillAskWhenGoingToACommit()
    {
        var (_, workspace, repository) = await OpenDuringAsync(Snapshots.Create(operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge", "fp", "")));
        await using var _ = workspace;
        var commit = Snapshots.Commit(Snapshots.Id('c'), "Elsewhere");
        Assert.True(repository.Branches.CanGoTo);
        Assert.NotNull(await repository.Branches.GoToAsync(commit));
    }

    // Deleting it reaches the remote.
    [Fact]
    public async Task WhenDeletingARemoteBranchIsRequested_ShouldWaitForTheUserWithoutAWrite()
    {
        var (writer, workspace, repository, list) = await OpenWithMergedAndUnmergedBranchesAsync();
        await using var _ = workspace;
        Assert.True(await repository.Branches.RequestDeleteAsync(list.Remote.Single()));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenDeletingAMergedLocalBranchIsRequested_ShouldDeleteItAtOnce()
    {
        var (_, workspace, repository, list) = await OpenWithMergedAndUnmergedBranchesAsync();
        await using var _ = workspace;
        Assert.False(await repository.Branches.RequestDeleteAsync(list.Local.Single(branch => branch.Name == "merged")));
    }

    [Fact]
    public async Task WhenGitRefusesToDeleteALocalBranchAsUnmerged_ShouldWaitForTheUser()
    {
        var (_, workspace, repository, list) = await OpenWithMergedAndUnmergedBranchesAsync();
        await using var _ = workspace;
        Assert.True(await repository.Branches.RequestDeleteAsync(list.Local.Single(branch => branch.Name == "unmerged")));
    }

    [Fact]
    public async Task WhenDeletingLocalBranchesIsRequested_ShouldAskGitWithoutForce()
    {
        var (writer, workspace, repository, list) = await OpenWithMergedAndUnmergedBranchesAsync();
        await using var _ = workspace;

        await repository.Branches.RequestDeleteAsync(list.Local.Single(branch => branch.Name == "merged"));
        await repository.Branches.RequestDeleteAsync(list.Local.Single(branch => branch.Name == "unmerged"));

        var deletes = writer.Requests.OfType<BranchRequest>().ToArray();
        Assert.Equal(["merged", "unmerged"], deletes.Select(request => request.Branch.Name));
        Assert.All(deletes, request => Assert.Equal((BranchAction.Delete, false), (request.Action, request.Branch.ForceAt is not null)));
    }

    // One started at HEAD holds what the detached HEAD has of its own; one not switched to leaves HEAD where it is.
    [Fact]
    public async Task WhenANewBranchDoesNotMoveADetachedHead_ShouldBeWrittenAtOnce()
    {
        var (workspace, repository) = await OpenDetachedForCreatesAsync();
        await using var _ = workspace;
        List<string> submitted = [];
        Func<Task<GitWriteResult?>> Submit(string name) => () => { submitted.Add(name); return Task.FromResult<GitWriteResult?>(null); };

        await repository.Branches.CreateAsync(new("here", null, Switch: true), Submit("here"));
        await repository.Branches.CreateAsync(new("kept", "refs/heads/main", Switch: false), Submit("kept"));

        Assert.Equal(["here", "kept"], submitted); Assert.Null(repository.Branches.Asking);
    }

    [Fact]
    public async Task WhenANewBranchStartsElsewhereAndIsSwitchedTo_ShouldWaitForTheDetachedHeadDecision()
    {
        var (workspace, repository) = await OpenDetachedForCreatesAsync();
        await using var _ = workspace;
        List<string> submitted = [];
        Func<Task<GitWriteResult?>> Submit(string name) => () => { submitted.Add(name); return Task.FromResult<GitWriteResult?>(null); };
        await repository.Branches.CreateAsync(new("here", null, Switch: true), Submit("here"));
        await repository.Branches.CreateAsync(new("kept", "refs/heads/main", Switch: false), Submit("kept"));

        await repository.Branches.CreateAsync(new("elsewhere", "refs/heads/main", Switch: true), Submit("elsewhere"));

        Assert.Equal(["here", "kept"], submitted); Assert.NotNull(repository.Branches.Asking);
    }

    // main and topic, opened, which reads the branches; Git refuses every write.
    private static async Task<(FakeBranchReader Branches, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository)> OpenWithARefusingGitAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "topic" } }; var writer = new FakeGitWriter { Result = new(GitWriteOutcome.Rejected, "refused") };
        var reader = new FakeRepositoryReader();
        var workspace = new RepositoryWorkspace(reader, writer: writer, branchReader: branches);
        var repository = new Repository(Services(workspace));
        // A write's verified snapshot reaches the repository, as the session's does.
        repository.Writer.ApplySnapshotAsync = snapshot => { repository.Apply(snapshot); return Task.CompletedTask; };
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Branches.List.Completion;
        return (branches, reader, workspace, repository);
    }

    // main checked out and topic beside it, origin/main, origin/topic and origin/new, opened with the branches read.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, Repository Repository, Dictionary<string, BranchInfo> Remote)> OpenWithRemoteBranchesAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "topic" }, Remote = { "origin/main", "origin/topic", "origin/new" } };
        var writer = new FakeGitWriter();
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: writer, branchReader: branches);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        await repository.Branches.List.Completion;
        return (writer, workspace, repository, repository.Branches.List.Value.Remote.ToDictionary(branch => branch.Name));
    }

    // A detached HEAD at b with one commit of its own and main to switch to, opened.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository)> OpenDetachedWithACommitOfItsOwnAsync(
        params string[] otherBranches)
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: null, head: Snapshots.Id('b'), detachedCommits: 1) };
        var branches = new FakeBranchReader { Local = { "main" } };
        branches.Local.AddRange(otherBranches);
        var workspace = new RepositoryWorkspace(reader, writer: writer, branchReader: branches);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, reader, workspace, repository);
    }

    private static Func<Task<GitWriteResult?>> SwitchToMain(BranchesModel branches) => () => branches.WriteAsync(BranchAction.Switch, new("main"));

    // The commits kept once as a branch and the switch run: two requests so far, and fake Git leaves HEAD detached at b.
    private static async Task<(FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository)> KeptOnceAsync()
    {
        var opened = await OpenDetachedWithACommitOfItsOwnAsync();
        var branches = opened.Repository.Branches;
        await branches.LeaveDetachedThenAsync(SwitchToMain(branches));
        await branches.KeepThenLeaveAsync("kept");
        return opened;
    }

    // main checked out, topic at commit b, and origin/main, opened, which reads the branches.
    private static async Task<(FakeBranchReader Branches, FakeGitWriter Writer, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, RepositoryServices Services)> OpenWithATopicTipAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "topic" }, Remote = { "origin/main" }, Heads = { ["topic"] = Snapshots.Id('b') } };
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader();
        var workspace = new RepositoryWorkspace(reader, writer: writer, branchReader: branches);
        var services = Services(workspace);
        var repository = new Repository(services);
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Branches.List.Completion;
        return (branches, writer, reader, workspace, repository, services);
    }

    private static async Task<(FakeBranchReader Branches, RepositoryWorkspace Workspace, Repository Repository)> OpenDuringAsync(RepositorySnapshot snapshot)
    {
        var branches = new FakeBranchReader { Local = { "main" } };
        var reader = new FakeRepositoryReader { Snapshot = snapshot };
        var workspace = new RepositoryWorkspace(reader, writer: new FakeGitWriter(), branchReader: branches);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Branches.List.Completion;
        return (branches, workspace, repository);
    }

    // main, merged and unmerged (Git refuses to delete it as not fully merged), and origin/topic, opened with the branches read.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, Repository Repository, BranchCatalog List)> OpenWithMergedAndUnmergedBranchesAsync()
    {
        var branches = new FakeBranchReader { Local = { "main", "merged", "unmerged" }, Remote = { "origin/topic" } };
        var writer = new FakeGitWriter
        {
            Handle = (request, _) => Task.FromResult(request is BranchRequest { Branch.Name: "unmerged" }
                ? new GitWriteResult(GitWriteOutcome.NeedsResolution, "not fully merged") : new GitWriteResult(GitWriteOutcome.Succeeded, "Deleted"))
        };
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), writer: writer, branchReader: branches);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        await repository.Branches.List.Completion;
        return (writer, workspace, repository, repository.Branches.List.Value);
    }

    // A detached HEAD at b with one commit of its own and no branch read as current, opened.
    private static async Task<(RepositoryWorkspace Workspace, Repository Repository)> OpenDetachedForCreatesAsync()
    {
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: null, head: Snapshots.Id('b'), detachedCommits: 1) };
        var workspace = new RepositoryWorkspace(reader, writer: new FakeGitWriter(), branchReader: new FakeBranchReader { Current = null });
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (workspace, repository);
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());
}
