using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Sync;

// The file status banner over the real workspace and a fake remote: it shows only for a branch rewritten after it was
// pushed, asks before replacing the remote branch, sends the force push the sync side runs, and goes once they match.
// Pull stays off while the branch is rewritten.
public sealed class ForcePushViewModelTests
{
    private static readonly RepositorySnapshot Trunk =
        new("fixture", "2.43.0", "trunk", Snapshots.HeadId, "origin/trunk", "origin", [new("origin", "https://fetch.example.invalid/repo", "https://publish.example.invalid/repo")],
            RepositoryOperation.None, [], []) { UpstreamBranchRef = "refs/heads/trunk" };

    private static FakeSyncWorkspace Backend(bool rewritten)
    {
        var (ahead, behind) = (2, 3);
        var backend = new FakeSyncWorkspace { Snapshot = Trunk, Result = new(GitWriteOutcome.Succeeded, "The remote branch was replaced.") };
        backend.ReadSync = (_, remote, _) => SyncViewModelTests.State(remote, "refs/heads/trunk", ahead, behind) with
        {
            Rewritten = rewritten && behind > 0,
            TrackingHead = Snapshots.Id('e')
        };
        backend.Handle = request =>
        {
            if (request is SyncRequest { Action: SyncAction.ForcePush }) (ahead, behind) = (0, 0);
            return backend.Result;
        };
        return backend;
    }

    private static async Task<ShellViewModel> Open(FakeSyncWorkspace backend)
    {
        var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        return model;
    }

    [Fact]
    public async Task WhenTheBranchWasRewrittenAfterItWasPushed_ShouldReplaceTheRemoteBranchOnlyAfterTheQuestionIsAnswered()
    {
        var backend = Backend(rewritten: true);
        await using var model = await Open(backend);
        var banner = model.FileStatusPage.ForcePush;
        Assert.True(banner.Summary.Shows);
        Assert.Equal("trunk no longer matches origin/trunk", banner.Summary.Title);
        Assert.StartsWith("It was rewritten after it was pushed: 2 commits here are new to the remote, and 3 there are no longer part of it.", banner.Summary.Text);
        Assert.True(banner.RequestForcePushCommand.CanExecute(null)); Assert.False(banner.Replace.ConfirmCommand.CanExecute(null));
        Assert.False(model.Sync.PullCommand.CanExecute(null)); Assert.False(model.Sync.Panel.Counts.SuggestsPull);

        banner.RequestForcePushCommand.Execute(null);
        Assert.True(banner.Replace.IsOpen);
        Assert.Equal("Replace origin/trunk with trunk?", banner.Summary.ConfirmTitle);
        Assert.Contains("still at eeeeeee", banner.Summary.ConfirmText);
        banner.Replace.CancelCommand.Execute(null);
        Assert.False(banner.Replace.IsOpen);
        Assert.Null(backend.LastRequest);

        banner.RequestForcePushCommand.Execute(null);
        await banner.Replace.ConfirmCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        var request = Assert.IsType<SyncRequest>(backend.LastRequest);
        Assert.Equal((SyncAction.ForcePush, Snapshots.Id('e')), (request.Action, request.Sync.TrackingHead));
        Assert.False(banner.Replace.IsOpen);
        Assert.False(banner.Summary.Shows);
        Assert.Equal("The remote branch was replaced.", model.StatusBar.Status);
        Assert.True(model.Sync.PullCommand.CanExecute(null));
    }

    // The remote may refuse the lease: the question stays while the push runs and then says why.
    [Fact]
    public async Task WhenTheForcePushRunsOrTheRemoteRefusesIt_ShouldKeepTheQuestionOpenAndShowTheReasonInIt()
    {
        var backend = Backend(rewritten: true);
        backend.Handle = _ => GitWriteResult.Rejected("The remote branch moved since it was read.");
        await using var model = await Open(backend);
        var banner = model.FileStatusPage.ForcePush;
        banner.RequestForcePushCommand.Execute(null);

        backend.Gate.IsClosed = true;
        var pending = banner.Replace.ConfirmCommand.ExecuteAsync(null); await backend.Gate.Started;
        Assert.True(banner.Replace.IsOpen); Assert.True(banner.Replace.IsRunning);
        backend.Gate.Open(); await pending; await model.Sync.SyncCompletion;

        Assert.Equal(SyncAction.ForcePush, Assert.IsType<SyncRequest>(backend.LastRequest).Action);
        Assert.True(banner.Summary.Shows);
        Assert.True(banner.Replace.IsOpen); Assert.Equal("The remote branch moved since it was read.", banner.Replace.Message);
    }

    // The question belongs to the branch it was asked about: a banner that goes away takes it along.
    [Fact]
    public async Task WhenTheBannerGoesAwayWhileAsking_ShouldCloseTheQuestion()
    {
        var backend = Backend(rewritten: true);
        await using var model = await Open(backend);
        var banner = model.FileStatusPage.ForcePush;
        banner.RequestForcePushCommand.Execute(null);

        backend.ReadSync = (_, remote, _) => SyncViewModelTests.State(remote, "refs/heads/trunk", 0, 0) with { TrackingHead = Snapshots.Id('e') };
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;

        Assert.False(banner.Summary.Shows); Assert.False(banner.Replace.IsOpen);
    }

    [Fact]
    public async Task WhenTheRemoteMovedOnFromTheBranch_ShouldShowNoBannerAndOfferNoForcePush()
    {
        var backend = Backend(rewritten: false);
        await using var model = await Open(backend);
        Assert.Equal((2, 3), (model.Sync.Panel.State!.Ahead, model.Sync.Panel.State.Behind));
        Assert.False(model.FileStatusPage.ForcePush.Summary.Shows);
        Assert.False(model.FileStatusPage.ForcePush.RequestForcePushCommand.CanExecute(null));
        Assert.False(model.Repository.Remotes.CanForcePush);
        Assert.True(model.Sync.PullCommand.CanExecute(null)); Assert.True(model.Sync.Panel.Counts.SuggestsPull);
    }
}
