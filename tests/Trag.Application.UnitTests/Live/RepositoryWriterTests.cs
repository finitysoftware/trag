using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class RepositoryWriterTests
{
    [Fact]
    public async Task WhenTheVerifiedSnapshotIsStillBeingApplied_ShouldKeepWritingAndNotReportYet()
    {
        var (_, _, repository, release, writing) = ApplyingTheSnapshot();
        Assert.True(repository.IsWriting);
        Assert.False(writing.IsCompleted);
        release.SetResult();
        await writing;
    }

    // The application goes while the write's snapshot is still being applied.
    [Fact]
    public async Task WhenTheRepositoryIsRetiredWhileTheSnapshotIsApplied_ShouldDropTheResultAndLeaveTheStatusAlone()
    {
        var (runner, bar, repository, release, writing) = ApplyingTheSnapshot();
        repository.Retire();
        bar.Status = "Closing";
        release.SetResult();
        Assert.Null(await writing);
        Assert.Null(runner.LastWrite); Assert.Null(runner.Running);
        Assert.Equal("Closing", bar.Status);
        Assert.False(repository.IsBusy);
    }

    [Fact]
    public async Task WhenASecondWriteComesWhileTheFirstOwnsTheGate_ShouldRefuseItWithoutStartingIt()
    {
        var (workspace, runner, repository, request, first) = await FirstOwningTheGateAsync();
        Assert.Null(await runner.RunAsync(request));
        Assert.Equal(1, workspace.Gate.Entries);
        Assert.True(repository.IsWriting);
        workspace.Gate.Open();
        await first;
    }

    [Fact]
    public async Task WhenTheFirstWriteFinishesAfterASecondWasRefused_ShouldStillReportIt()
    {
        var (workspace, runner, repository, request, first) = await FirstOwningTheGateAsync();
        Assert.Null(await runner.RunAsync(request));
        workspace.Gate.Open();
        Assert.NotNull(await first);
        Assert.False(repository.IsWriting);
    }

    // Choosing the account a remote uses needs nothing of the repository's state, but it is still refused while
    // another write runs, whose Cancel it would otherwise take over.
    [Fact]
    public async Task WhenAnAccountIsChosenWhileAnotherWriteRuns_ShouldRefuseItAndLeaveTheRunningWriteAlone()
    {
        var (workspace, runner, repository, _, first) = await FirstOwningTheGateAsync();
        var choosing = runner.RunAsync(Sync(repository.Snapshot!, SyncAction.SelectAccount));
        Assert.True(choosing.IsCompleted);
        Assert.Null(await choosing);
        Assert.Equal(1, workspace.Gate.Entries); Assert.False(workspace.Gate.FirstToken.IsCancellationRequested);
        workspace.Gate.Open();
        await first;
    }

    [Fact]
    public async Task WhenAWriteOfARetiredRepositoryFails_ShouldNotMarkItForReview()
    {
        var workspace = Workspace();
        workspace.Gate.IsClosed = true;
        var (runner, _, repository) = Runner(workspace);
        var writing = runner.RunAsync(Stage(repository.Snapshot!));
        await workspace.Gate.Started;
        repository.Retire();
        workspace.Gate.Fail(new IOException("retired"));
        Assert.Null(await writing);
        Assert.Null(runner.LastWrite);
        Assert.False(repository.NeedsReview);
        Assert.False(repository.IsWriting);
    }

    [Fact]
    public async Task WhenASyncWaitsForGit_ShouldBeBusyAndShowItsStatusInItsPanelAndHearItsProgress()
    {
        var (workspace, bar, repository, _, _, task) = await FetchingBehindTheGateAsync();
        Assert.True(repository.IsBusy); Assert.True(repository.IsWriting);
        Assert.Equal("Fetching…", bar.Status); Assert.Equal("Fetching…", bar.SyncOperationText); Assert.NotNull(workspace.LastRequest!.Progress);
        workspace.Gate.Open();
        await task;
    }

    // The views that follow the end of every write leave a sync to its own panel.
    [Fact]
    public async Task WhenAFetchWaitsForGit_ShouldSayASyncRuns()
    {
        var workspace = Workspace(); workspace.Gate.IsClosed = true;
        var (runner, _, repository) = Runner(workspace);
        var fetching = runner.RunAsync(Sync(repository.Snapshot!, SyncAction.Fetch));
        await workspace.Gate.Started;
        Assert.True(runner.RunsSync);
        workspace.Gate.Open();
        await fetching;
    }

    [Fact]
    public async Task WhenGitReportsProgressWhileASyncRuns_ShouldShowItOnTheStatusLineAndInTheSyncPanel()
    {
        var (workspace, bar, _, _, _, task) = await FetchingBehindTheGateAsync();
        workspace.LastRequest!.Progress!.Report("Receiving objects: 50%");
        Assert.Equal(("Receiving objects: 50%", "Receiving objects: 50%"), (bar.Status, bar.SyncOperationText));
        workspace.Gate.Open();
        await task;
    }

    [Fact]
    public async Task WhenAWriteFinishes_ShouldApplyItsSnapshotThenReportInOrderAndBeIdle()
    {
        var (workspace, _, repository, events, applied, task) = await FetchingBehindTheGateAsync();
        workspace.Gate.Open();
        var result = await task;
        Assert.Equal(GitWriteOutcome.Succeeded, result!.Outcome);
        Assert.Same(workspace.Result.Snapshot, Assert.Single(applied));
        Assert.Equal(["starting False SyncRequest", "apply", "completed True Done Done", "finished False"], events);
        Assert.False(repository.IsBusy); Assert.False(repository.IsWriting); Assert.False(repository.NeedsReview);
    }

    [Fact]
    public async Task WhenAWriteThrows_ShouldMarkItForReviewAndStillEndIt()
    {
        var workspace = Workspace(); workspace.Failure = new InvalidOperationException("boom");
        var (runner, _, repository) = Runner(workspace);
        var result = await runner.RunAsync(Stage(repository.Snapshot!));
        Assert.Null(result); Assert.Null(runner.Running); Assert.True(repository.NeedsReview); Assert.False(repository.IsWriting);
        Assert.IsType<StageRequest>(runner.LastWrite!.Request); Assert.Null(runner.LastWrite.Result); // the outcome could not be displayed
        Assert.False(repository.CanWrite); // review first
    }

    [Fact]
    public async Task WhenAFetchRuns_ShouldClearOnlyTheSyncDetails()
    {
        var (workspace, runner, bar) = WithEarlierReports();
        var fetching = runner.RunAsync(Sync(Snapshots.Create(), SyncAction.Fetch));
        await workspace.Gate.Started;
        Assert.Equal(("write", "", "Fetching…"), (bar.WriteDetails, bar.SyncDetails, bar.SyncOperationText));
        workspace.Gate.Open();
        await fetching;
    }

    [Fact]
    public async Task WhenAStagingWriteRuns_ShouldShowStagingAndClearEveryEarlierReport()
    {
        var (workspace, runner, bar) = WithEarlierReports();
        var staging = runner.RunAsync(Stage(Snapshots.Create()));
        await workspace.Gate.Started;
        Assert.Equal("Staging selected whole files…", bar.Status);
        Assert.Equal(("", "", ""), (bar.WriteDetails, bar.SyncDetails, bar.SyncOperationText));
        workspace.Gate.Open();
        await staging;
    }

    // Git's progress that arrives after the write ended does not replace how it ended.
    [Fact]
    public async Task WhenProgressArrivesAfterAPublishEnded_ShouldKeepItsResultOnTheStatusLine()
    {
        var workspace = Workspace();
        var (runner, bar, repository) = Runner(workspace);
        var heard = new List<string>();
        await runner.RunAsync(new PublishRequest(repository.Snapshot!, PublishOperation.PushTo("origin", "main", null)), heard.Add);
        workspace.LastRequest!.Progress!.Report("Writing objects: 50%");
        Assert.Equal(("Done", "Done"), (bar.Status, bar.SyncOperationText)); Assert.Empty(heard);
    }

    [Fact]
    public async Task WhenTheRepositoryIsRetiredWhileGitWrites_ShouldDropTheResult()
    {
        var workspace = Workspace(); workspace.Gate.IsClosed = true;
        var (runner, _, repository) = Runner(workspace);
        var applied = false;
        runner.ApplySnapshotAsync = _ => { applied = true; return Task.CompletedTask; };
        var task = runner.RunAsync(Stage(repository.Snapshot!));
        await workspace.Gate.Started;
        repository.Retire();
        workspace.Gate.Open();
        Assert.Null(await task); Assert.False(applied); Assert.Null(runner.LastWrite); Assert.False(repository.IsWriting);
    }

    // The writer asks the repository whether the kind of write may run, whatever the caller checked before.
    [Fact]
    public async Task WhenNoMergeIsInProgress_ShouldRefuseTheWritesThatBelongToOne()
    {
        var workspace = Workspace();
        var (runner, _, repository) = Runner(workspace);
        async Task<bool> Runs(GitWriteRequest request) => await runner.RunAsync(request) is not null;
        var plain = repository.Snapshot!;
        Assert.False(await Runs(new MergeAbortRequest(plain)));
        Assert.False(await Runs(new StageRequest(plain, [Snapshots.Path("a")], Resolution: true)));
    }

    [Fact]
    public async Task WhenAMergeTragUnderstandsIsInProgress_ShouldRefuseOrdinaryWritesWithoutStartingThem()
    {
        var workspace = Workspace();
        var (runner, _, repository) = Runner(workspace);
        async Task<bool> Runs(GitWriteRequest request) => await runner.RunAsync(request) is not null;
        repository.Apply(Merging());
        var merging = repository.Snapshot!;
        Assert.False(await Runs(Stage(merging)));
        Assert.False(await Runs(new CommitRequest(merging, "m")));
        Assert.Empty(workspace.Requests);
    }

    [Fact]
    public async Task WhenAMergeTragUnderstandsIsInProgress_ShouldRunItsOwnWrites()
    {
        var workspace = Workspace();
        var (runner, _, repository) = Runner(workspace);
        async Task<bool> Runs(GitWriteRequest request) => await runner.RunAsync(request) is not null;
        repository.Apply(Merging());
        var merging = repository.Snapshot!;
        Assert.True(await Runs(new StageRequest(merging, [Snapshots.Path("a")], Resolution: true)));
        Assert.True(await Runs(new CommitRequest(merging, "m", FinishesMerge: true)));
        Assert.True(await Runs(new MergeAbortRequest(merging)));
        Assert.Equal(3, workspace.Requests.Count);
    }

    // The three writes before the review are the ones a merge allows; only the account choice adds a fourth.
    [Fact]
    public async Task WhenAnOutcomeWaitsForReview_ShouldRefuseEveryWriteButChoosingTheAccountARemoteUses()
    {
        var workspace = Workspace();
        var (runner, _, repository) = Runner(workspace);
        async Task<bool> Runs(GitWriteRequest request) => await runner.RunAsync(request) is not null;
        repository.Apply(Merging());
        var merging = repository.Snapshot!;
        await Runs(new StageRequest(merging, [Snapshots.Path("a")], Resolution: true));
        await Runs(new CommitRequest(merging, "m", FinishesMerge: true));
        await Runs(new MergeAbortRequest(merging));
        repository.Apply(Snapshots.Create());
        repository.NeedsReview = true;
        Assert.False(await Runs(Stage(repository.Snapshot!)));
        Assert.False(await Runs(Sync(repository.Snapshot!, SyncAction.Fetch)));
        Assert.True(await Runs(Sync(repository.Snapshot!, SyncAction.SelectAccount)));
        Assert.Equal(4, workspace.Requests.Count);
    }

    // A write no command held back, as the branch picker's rows, which stay enabled for their menus, or one whose state
    // changed while its command awaited, is refused here with the reason on the status line; only a write that comes
    // while another runs leaves the status line to that one.
    [Theory]
    [InlineData("writing")]
    [InlineData("review")]
    [InlineData("reading")]
    [InlineData("merge")]
    public async Task WhenAWriteIsRefusedBeforeItStarts_ShouldSayWhyOnTheStatusLine(string state)
    {
        var workspace = Workspace();
        var (runner, bar, repository) = Runner(workspace);
        if (state == "merge") repository.Apply(Merging());
        repository.NeedsReview = state == "review";
        repository.IsReading = state == "reading";
        repository.IsWriting = state == "writing";
        bar.Status = "Shown";

        Assert.Null(await runner.RunAsync(Stage(repository.Snapshot!)));

        Assert.Equal(state switch { "writing" => "Shown", "review" => WriteText.ReviewFirst, "reading" => WriteText.ReadInProgress, _ => WriteText.StateUnsupported }, bar.Status);
        Assert.Empty(workspace.Requests);
    }

    // Choosing the account a remote uses is the one write an outcome waiting for review lets through, and it verifies
    // nothing of that outcome: the banner stays, and so does the review every other write waits for until F5.
    [Fact]
    public async Task WhenAnAccountIsSavedWhileTheLastWriteIsUncertain_ShouldKeepNeedsReviewAndTheBanner()
    {
        var workspace = Workspace(); workspace.Result = new(GitWriteOutcome.Uncertain, "Not verified");
        var (runner, bar, repository) = Runner(workspace);
        await runner.RunAsync(Stage(repository.Snapshot!));
        workspace.Result = new(GitWriteOutcome.Succeeded, "Account saved");

        await runner.RunAsync(Sync(repository.Snapshot!, SyncAction.SelectAccount));

        Assert.Equal((true, "Not verified"), (repository.NeedsReview, bar.ErrorMessage));
    }

    // A refusal's banner stays up until the next write starts; only an outcome waiting for review keeps it longer.
    [Fact]
    public async Task WhenAWriteFollowsARefusal_ShouldTakeTheRefusalsBannerDown()
    {
        var workspace = Workspace(); workspace.Result = new(GitWriteOutcome.Rejected, "Git says no");
        var (runner, bar, repository) = Runner(workspace);
        await runner.RunAsync(Stage(repository.Snapshot!));
        workspace.Result = new(GitWriteOutcome.Succeeded, "Staged");

        await runner.RunAsync(Stage(repository.Snapshot!));

        Assert.Equal("", bar.ErrorMessage);
    }

    [Theory]
    [InlineData(GitWriteOutcome.Succeeded, false)]
    [InlineData(GitWriteOutcome.Cancelled, false)]
    [InlineData(GitWriteOutcome.NeedsResolution, false)]
    [InlineData(GitWriteOutcome.Rejected, true)]
    [InlineData(GitWriteOutcome.Uncertain, true)]
    public async Task WhenAWriteEnds_ShouldLeaveGitsOutputUnderDetailsAndARefusalInTheErrorBanner(GitWriteOutcome outcome, bool error)
    {
        var workspace = Workspace(); workspace.Result = new(outcome, "Git says", "git output");
        var (runner, bar, repository) = Runner(workspace);
        await runner.RunAsync(Stage(repository.Snapshot!));
        Assert.Equal("Git says", bar.Status); Assert.Equal("git output", bar.WriteDetails);
        Assert.Equal(error ? "Git says" : "", bar.ErrorMessage);
    }

    [Fact]
    public async Task WhenASyncEnds_ShouldReportInItsOwnPanelAndLeaveTheErrorBannerAndTheLastWritesDetails()
    {
        var workspace = Workspace(); workspace.Result = new(GitWriteOutcome.Rejected, "Remote refused", "remote: denied");
        var (runner, bar, repository) = Runner(workspace);
        var snapshot = repository.Snapshot!;
        var fetch = Sync(snapshot, SyncAction.Fetch);
        bar.WriteDetails = "last write";
        await runner.RunAsync(fetch);
        Assert.Equal("Remote refused", bar.SyncOperationText); Assert.Equal("remote: denied", bar.SyncDetails);
        Assert.Equal("", bar.ErrorMessage); Assert.Equal("last write", bar.WriteDetails);
    }

    [Fact]
    public async Task WhenASyncThrowsBeforeGitCouldReport_ShouldSayInItsPanelThatTheOutcomeIsUnknown()
    {
        var workspace = Workspace(); workspace.Failure = new IOException("pipe closed");
        var (runner, bar, repository) = Runner(workspace);
        var snapshot = repository.Snapshot!;
        var fetch = Sync(snapshot, SyncAction.Fetch);
        Assert.Null(await runner.RunAsync(fetch));
        Assert.Equal(SyncText.ResultNotDisplayed, bar.SyncOperationText); Assert.Equal(SyncText.ResultNotDisplayed, bar.Status);
        Assert.Equal("", bar.ErrorMessage); Assert.True(repository.NeedsReview);
    }

    [Fact]
    public async Task WhenAnyOtherWriteThrowsBeforeGitCouldReport_ShouldSayInTheErrorBannerThatTheOutcomeIsUnknown()
    {
        var workspace = Workspace(); workspace.Failure = new IOException("pipe closed");
        var (runner, bar, repository) = Runner(workspace);
        var snapshot = repository.Snapshot!;
        Assert.Null(await runner.RunAsync(Stage(snapshot)));
        Assert.Equal(CommitText.ResultNotDisplayed, bar.ErrorMessage); Assert.Equal(CommitText.ResultNotDisplayed, bar.Status);
    }

    // What a write or its report throws past the workspace is a defect in Trag: it is recorded with its stack
    // where the crash logs are, and the user still gets only the sentence.
    [Fact]
    public async Task WhenAWriteThrows_ShouldRecordWhatItThrewAsADefect()
    {
        var workspace = Workspace(); workspace.Failure = new InvalidOperationException("defect");
        List<Exception> recorded = [];
        var bar = new StatusBarState();
        var repository = new Repository(new RepositoryServices(workspace, bar, new ImmediateViewDispatcher()) { Defects = recorded.Add });
        repository.Apply(Snapshots.Create());

        Assert.Null(await repository.Writer.RunAsync(Stage(repository.Snapshot!)));

        Assert.Same(workspace.Failure, Assert.Single(recorded));
        Assert.Equal(CommitText.ResultNotDisplayed, bar.ErrorMessage);
    }

    [Fact]
    public async Task WhenThereIsNoWriter_ShouldRunNothingAndLeaveTheStateUntouched()
    {
        var bar = new StatusBarState(); var repository = new Repository();
        var runner = new RepositoryWriter(null, bar, repository, new ImmediateViewDispatcher(), _ => { });
        Assert.Null(await runner.RunAsync(Stage(Snapshots.Create())));
        // Choosing an account needs nothing of the repository, so only the missing writer refuses it.
        Assert.Null(await runner.RunAsync(Sync(Snapshots.Create(), SyncAction.SelectAccount)));
        Assert.Null(runner.Running); Assert.False(repository.IsWriting); Assert.False(repository.NeedsReview);
    }

    private static GitWriteRequest Stage(RepositorySnapshot snapshot) => new StageRequest(snapshot, [Snapshots.Path("a")]);
    private static SyncRequest Sync(RepositorySnapshot snapshot, SyncAction action) => new(snapshot, FakeGitSync.DefaultState(snapshot, "origin", null), action);
    private static RepositorySnapshot Merging() =>
        Snapshots.Create(operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge", "fp", ""));

    // Every write answers "Done" with the status that follows it, the way the real workspace re-reads before reporting.
    private static FakeRepositoryWorkspace Workspace() =>
        new() { Result = new(GitWriteOutcome.Succeeded, "Done") { Snapshot = Snapshots.Create(files: [Snapshots.Staged("a")]) } };

    private static (RepositoryWriter Runner, StatusBarState Bar, Repository Repository) Runner(FakeRepositoryWorkspace workspace)
    {
        var bar = new StatusBarState(); var repository = new Repository(hasWriter: true);
        repository.Apply(Snapshots.Create());
        return (new RepositoryWriter(workspace, bar, repository, new ImmediateViewDispatcher(), _ => { }), bar, repository);
    }

    // A write whose verified snapshot is being applied until Release is set.
    private static (RepositoryWriter Runner, StatusBarState Bar, Repository Repository, TaskCompletionSource Release, Task<GitWriteResult?> Writing) ApplyingTheSnapshot()
    {
        var (runner, bar, repository) = Runner(Workspace());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.ApplySnapshotAsync = _ => release.Task;
        var writing = runner.RunAsync(Stage(repository.Snapshot!));
        return (runner, bar, repository, release, writing);
    }

    // A first write that entered the workspace's closed gate and waits there; Request is the request it runs.
    private static async Task<(FakeRepositoryWorkspace Workspace, RepositoryWriter Runner, Repository Repository, GitWriteRequest Request, Task<GitWriteResult?> First)> FirstOwningTheGateAsync()
    {
        var workspace = Workspace();
        workspace.Gate.IsClosed = true;
        var (runner, _, repository) = Runner(workspace);
        var request = Stage(repository.Snapshot!);
        var first = runner.RunAsync(request);
        await workspace.Gate.Started;
        return (workspace, runner, repository, request, first);
    }

    // A fetch waiting for Git behind the workspace's gate. Events records when the write starts, applies its snapshot,
    // completes and finishes, with the state at each step.
    private static async Task<(FakeRepositoryWorkspace Workspace, StatusBarState Bar, Repository Repository, List<string> Events, List<RepositorySnapshot> Applied, Task<GitWriteResult?> Writing)> FetchingBehindTheGateAsync()
    {
        var workspace = Workspace(); workspace.Gate.IsClosed = true;
        var (runner, bar, repository) = Runner(workspace);
        var events = new List<string>();
        var applied = new List<RepositorySnapshot>();
        runner.ApplySnapshotAsync = snapshot => { applied.Add(snapshot); events.Add("apply"); return Task.CompletedTask; };
        runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepositoryWriter.Running) && runner.Running is { } request) events.Add("starting " + repository.IsWriting + " " + request.GetType().Name);
            else if (e.PropertyName == nameof(RepositoryWriter.Running)) events.Add("finished " + repository.IsWriting);
            else if (e.PropertyName == nameof(RepositoryWriter.LastWrite)) events.Add("completed " + repository.IsWriting + " " + runner.LastWrite!.Result!.Message + " " + bar.Status);
        };
        var task = runner.RunAsync(Sync(repository.Snapshot!, SyncAction.Fetch));
        await workspace.Gate.Started;
        return (workspace, bar, repository, events, applied, task);
    }

    // A writer whose writes wait behind the workspace's gate, and a status bar that still shows the details of an earlier
    // write and an earlier sync.
    private static (FakeRepositoryWorkspace Workspace, RepositoryWriter Runner, StatusBarState Bar) WithEarlierReports()
    {
        var workspace = Workspace(); workspace.Gate.IsClosed = true;
        var (runner, bar, _) = Runner(workspace);
        bar.WriteDetails = "write"; bar.SyncDetails = "sync"; bar.SyncOperationText = "op";
        return (workspace, runner, bar);
    }
}
