using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Application.Ports.Threading;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Merge;

// The writes a confirmed merge takes, in order: one merge into the current branch; a switch there and back for another
// branch; no way back while Git leaves a merge to resolve; a push while the target is still checked out.
public sealed class MergeFlowTests
{
    private static readonly BranchInfo Main = new("main", "refs/heads/main", Snapshots.HeadId, false, null, "refs/remotes/origin/main");
    private static readonly BranchInfo Feature = new("feature", "refs/heads/feature", Snapshots.Id('b'), false);
    private static readonly BranchInfo RemoteFeature = new("origin/feature", "refs/remotes/origin/feature", Snapshots.Id('b'), true, "origin");
    private static readonly MergePreview Preview = new(Snapshots.HeadId, Snapshots.Id('b'), [], 2, 1, []);
    private static readonly GitWriteResult Merged = new(GitWriteOutcome.Succeeded, "Merged.");

    private sealed record Setup(MergeFlow Flow, FakeSyncWorkspace Workspace, Repository Repository, StatusBarState Bar)
    {
        public IEnumerable<string> Writes => Workspace.Requests.Select(request => request switch
        {
            BranchRequest branch => "switch " + branch.Branch.Name,
            MergeRequest merge => "merge " + merge.FromName,
            SyncRequest sync => sync.Action.ToString().ToLowerInvariant(),
            _ => request.GetType().Name
        });
    }

    // The workspace switches branches the way Git would and answers the merge with the given result; the writer applies
    // every verified snapshot, so each write is built on what the previous one left.
    private static Setup Create(string current, GitWriteResult merge, bool tracked = false, GitWriteResult? switchResult = null, GitWriteResult? download = null)
    {
        var workspace = new FakeSyncWorkspace { Snapshot = Snapshot(current, tracked) };
        workspace.Handle = request => request switch
        {
            LfsDownloadRequest => download ?? new(GitWriteOutcome.Succeeded, ""),
            BranchRequest { Action: BranchAction.Switch } branch when switchResult is null || branch.Branch.Name == current => Switch(workspace, branch.Branch.Name, tracked),
            BranchRequest => switchResult!,
            MergeRequest => merge,
            _ => new(GitWriteOutcome.Succeeded, "Pushed.")
        };
        var bar = new StatusBarState(); var repository = new Repository(new RepositoryServices(workspace, bar, new ImmediateViewDispatcher()));
        repository.Apply(workspace.Snapshot);
        repository.Writer.ApplySnapshotAsync = snapshot => { repository.Apply(snapshot); return Task.CompletedTask; };
        return new(new MergeFlow(repository.Writer, repository.Remotes, bar), workspace, repository, bar);
    }

    private static RepositorySnapshot Snapshot(string branch, bool tracked) => tracked
        ? Snapshots.Create(branch: branch, remotes: [Snapshots.Remote()], trackingRemote: "origin", upstream: "origin/" + branch)
        : Snapshots.Create(branch: branch);

    private static GitWriteResult Switch(FakeSyncWorkspace workspace, string branch, bool tracked)
    {
        workspace.Snapshot = Snapshot(branch, tracked);
        return new(GitWriteOutcome.Succeeded, "Switched to " + branch + ".");
    }

    [Fact]
    public async Task WhenTheMergeGoesIntoTheCurrentBranch_ShouldBeOneMergeOfThePreviewedCommit()
    {
        var s = Create("main", Merged);
        var result = await s.Flow.RunAsync(new(Feature, Main, "main") { NoFastForward = true, Message = "Bring it in" }, Preview);
        Assert.Equal((GitWriteOutcome.Succeeded, "Merged."), (result!.Outcome, result.Message));
        var merge = Assert.IsType<MergeRequest>(Assert.Single(s.Workspace.Requests));
        Assert.Equal(("feature", Snapshots.Id('b'), true, "Bring it in"), (merge.FromName, merge.FromHead, merge.NoFastForward, merge.Message));
        Assert.Equal("Merged.", s.Bar.Status);
    }

    // A remote branch's Git LFS content comes down with its remote's credentials before the merge checks it out.
    [Fact]
    public async Task WhenARemoteBranchIsMerged_ShouldDownloadItsLfsContentFirst()
    {
        var s = Create("main", Merged);
        var result = await s.Flow.RunAsync(new(RemoteFeature, Main, "main"), Preview);
        Assert.Equal(["LfsDownloadRequest", "merge origin/feature"], s.Writes);
        var download = Assert.IsType<LfsDownloadRequest>(s.Workspace.Requests[0]);
        Assert.Equal(("origin", Snapshots.Id('b')), (download.Remote, download.Commit));
        Assert.Equal("Merged.", result!.Message);
    }

    [Fact]
    public async Task WhenTheLfsContentCannotBeDownloaded_ShouldNotMerge()
    {
        var s = Create("main", Merged, download: GitWriteResult.Rejected("Not downloaded."));
        var result = await s.Flow.RunAsync(new(RemoteFeature, Main, "main"), Preview);
        Assert.Equal(["LfsDownloadRequest"], s.Writes);
        Assert.Equal((GitWriteOutcome.Rejected, "Not downloaded."), (result!.Outcome, result.Message));
    }

    [Fact]
    public async Task WhenTheMergeGoesIntoAnotherBranch_ShouldSwitchThereAndBack()
    {
        var s = Create("main", Merged);
        var result = await s.Flow.RunAsync(new(Main, Feature, "main"), Preview);
        Assert.Equal(["switch feature", "merge main", "switch main"], s.Writes);
        Assert.Equal(GitWriteOutcome.Succeeded, result!.Outcome);
        Assert.Equal("Merged. Back on main.", result.Message);
        Assert.Equal("Merged. Back on main.", s.Bar.Status);
        Assert.Equal("main", s.Repository.Snapshot!.Branch);
    }

    [Fact]
    public async Task WhenTheUserStaysOnTheTarget_ShouldNotSwitchBack()
    {
        var s = Create("main", Merged);
        await s.Flow.RunAsync(new(Main, Feature, "main") { StayOnTarget = true }, Preview);
        Assert.Equal(["switch feature", "merge main"], s.Writes);
        Assert.Equal("feature", s.Repository.Snapshot!.Branch);
    }

    // Staying is for a merge that happened.
    [Fact]
    public async Task WhenAMergeToStayOnIsRejected_ShouldStillSwitchBack()
    {
        var s = Create("main", GitWriteResult.Rejected("Merge was rejected."));
        await s.Flow.RunAsync(new(Main, Feature, "main") { StayOnTarget = true }, Preview);
        Assert.Equal(["switch feature", "merge main", "switch main"], s.Writes);
        Assert.Equal("main", s.Repository.Snapshot!.Branch);
    }

    [Fact]
    public async Task WhenGitLeavesTheMergeToResolve_ShouldKeepTheTargetCheckedOutAndSaySo()
    {
        var s = Create("main", new(GitWriteOutcome.NeedsResolution, "Merge left conflicts to resolve."));
        var result = await s.Flow.RunAsync(new(Main, Feature, "main"), Preview);
        Assert.Equal(["switch feature", "merge main"], s.Writes);
        Assert.Equal(GitWriteOutcome.NeedsResolution, result!.Outcome);
        Assert.Equal("Merge left conflicts to resolve. You stay on feature until the merge is finished or aborted.", result.Message);
    }

    // A merge left for its conflicts is not the merge to push: the push waits for its own click after Finish.
    [Fact]
    public async Task WhenTheMergeNeedsResolutionAndPushAfterWasChosen_ShouldNotPush()
    {
        var s = Create("main", new(GitWriteOutcome.NeedsResolution, "Merge left conflicts to resolve."), tracked: true);
        var result = await s.Flow.RunAsync(new(Main, Feature, "main") { PushAfter = true }, Preview);
        Assert.Equal(["switch feature", "merge main"], s.Writes);
        Assert.Equal(GitWriteOutcome.NeedsResolution, result!.Outcome);
    }

    [Fact]
    public async Task WhenTheMergeIntoAnotherBranchIsRejected_ShouldStillSwitchBack()
    {
        var rejected = Create("main", GitWriteResult.Rejected("Merge was rejected."));
        var result = await rejected.Flow.RunAsync(new(Main, Feature, "main"), Preview);
        Assert.Equal(["switch feature", "merge main", "switch main"], rejected.Writes);
        Assert.Equal((GitWriteOutcome.Rejected, "Merge was rejected. Back on main."), (result!.Outcome, result.Message));
    }

    // The merge stands; the user learns they are still on the target, and why Git would not switch back when it said why.
    [Theory]
    [InlineData(false, "Merged. Trag could not switch back to main: Git refused to switch.")]
    [InlineData(true, "Merged. Trag could not switch back to main.")]
    public async Task WhenSwitchingBackFails_ShouldSayNotSwitchedBack(bool throws, string message)
    {
        var s = Create("main", Merged);
        var handle = s.Workspace.Handle!;
        s.Workspace.Handle = request => request is not BranchRequest { Branch.Name: "main" } ? handle(request)
            : throws ? throw new InvalidOperationException("The switch failed.") : GitWriteResult.Rejected("Git refused to switch.");

        var result = await s.Flow.RunAsync(new(Main, Feature, "main"), Preview);

        Assert.Equal(["switch feature", "merge main", "switch main"], s.Writes);
        Assert.Equal((GitWriteOutcome.Succeeded, message), (result!.Outcome, result.Message));
        Assert.Equal("feature", s.Repository.Snapshot!.Branch);
    }

    [Fact]
    public async Task WhenGitRefusesToSwitchToTheTarget_ShouldStopEverything()
    {
        var refused = Create("main", Merged, switchResult: GitWriteResult.Rejected("Git refused to switch."));
        var result = await refused.Flow.RunAsync(new(Main, Feature, "main"), Preview);
        Assert.Equal(["switch feature"], refused.Writes);
        Assert.Equal("Git refused to switch.", result!.Message);
    }

    // Push sends the checked-out branch, so it runs before the way back.
    [Fact]
    public async Task WhenAPushFollowsAndTheTargetHasAnUpstream_ShouldPushWhileTheTargetIsCheckedOut()
    {
        var s = Create("main", Merged, tracked: true);
        var result = await s.Flow.RunAsync(new(Main, Feature, "main") { PushAfter = true }, Preview);
        Assert.Equal(["switch feature", "merge main", "push", "switch main"], s.Writes);
        // The push reads the upstream the target tracks; the repository's remotes read their own target besides.
        Assert.Contains(("origin", "refs/heads/feature"), s.Workspace.SyncReads);
        Assert.Equal("Merged. Pushed feature to its upstream. Back on main.", result!.Message);
    }

    // The upstream the push goes to is read just before it: a read that fails pushes nothing and says so, after the merge.
    [Fact]
    public async Task WhenAPushFollowsAndItsUpstreamCannotBeRead_ShouldMergeAndSayThePushDidNotStart()
    {
        var s = Create("main", Merged, tracked: true);
        s.Workspace.ReadSync = (_, _, _) => throw new TimeoutException("Git did not answer.");

        var result = await s.Flow.RunAsync(new(Main, Feature, "main") { PushAfter = true }, Preview);

        Assert.Equal(["switch feature", "merge main", "switch main"], s.Writes);
        Assert.Equal("Merged. The push of feature did not complete: " + Failures.UpstreamNotRead + " Back on main.", result!.Message);
    }

    [Fact]
    public async Task WhenAPushFollowsAndTheTargetHasNoUpstream_ShouldMergeOnlyAndPointToTheSidebar()
    {
        var untracked = Create("main", Merged);
        var result = await untracked.Flow.RunAsync(new(Feature, Main, "main") { PushAfter = true }, Preview);
        Assert.Equal(["merge feature"], untracked.Writes);
        Assert.Equal("Merged. main has no upstream Trag can push to; publish or push it from the sidebar.", result!.Message);
    }

    [Fact]
    public async Task WhenNoBranchGivesTheCommits_ShouldRunNothing()
    {
        var s = Create("main", Merged);
        Assert.Null(await s.Flow.RunAsync(new(null, Main, "main"), Preview));
        Assert.Empty(s.Workspace.Requests);
    }

    [Fact]
    public async Task WhenThePreviewWasNotRead_ShouldRunNothing()
    {
        var s = Create("main", Merged);
        Assert.Null(await s.Flow.RunAsync(new(Feature, Main, "main"), MergePreview.None));
        Assert.Empty(s.Workspace.Requests);
    }
}
