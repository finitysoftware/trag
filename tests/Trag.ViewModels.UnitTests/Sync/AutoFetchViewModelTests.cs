using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Sync;

// The fetch Trag runs on its own, over the real shell and a fake remote with a hand-moved clock: what it fetches and
// when, what a refused sign-in and a failed network do to it, and that a write, the Off choice or a branch without an
// upstream keep it from running.
public sealed class AutoFetchViewModelTests
{
    private static readonly RepositorySnapshot Trunk =
        new("fixture", "2.43.0", "trunk", Snapshots.HeadId, "origin/trunk", "origin", [new("origin", "https://fetch.example.invalid/repo", "https://publish.example.invalid/repo")],
            RepositoryOperation.None, [], []) { UpstreamBranchRef = "refs/heads/trunk" };

    private static async Task<(FakeSyncWorkspace Backend, ShellViewModel Model, ManualTime Time)> OpenAsync(string? branch = "refs/heads/trunk")
    {
        var backend = new FakeSyncWorkspace { Snapshot = Trunk, Result = new(GitWriteOutcome.Succeeded, "Fetched.") };
        var time = new ManualTime();
        backend.ReadSync = (_, remote, _) => SyncViewModelTests.State(remote, branch);
        var model = ShellHarness.Compose(backend, time: time);
        model.Workbench.AutoFetch.Run();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        return (backend, model, time);
    }

    [Fact]
    public async Task WhenTheClockMoves_ShouldFetchTheShownUpstreamAtTheFirstTickAndThenOncePerInterval()
    {
        var (backend, model, time) = await OpenAsync();
        await using var _ = model;
        var reads = backend.SyncReads.Count;
        await model.Workbench.AutoFetch.TickAsync();
        await model.Sync.SyncCompletion;
        Assert.Equal(reads + 1, backend.SyncReads.Count);
        Assert.Equal("Fetch (Ctrl+Shift+F) · Trag also fetches every minute", model.AutoFetch.FetchTip);

        time.Now = time.Now.AddSeconds(14);
        await model.Workbench.AutoFetch.ActivatedAsync();
        time.Now = time.Now.AddSeconds(45);
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Single(backend.QuietFetches);
        time.Now = time.Now.AddSeconds(1);
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Equal(2, backend.QuietFetches.Count);
        time.Now = time.Now.AddSeconds(15);
        await model.Workbench.AutoFetch.ActivatedAsync();
        Assert.Equal(3, backend.QuietFetches.Count);
    }

    [Fact]
    public async Task WhenTheSignInIsRefused_ShouldStopFetchingWithANoticeUntilTheUsersOwnFetchSucceeds()
    {
        var (backend, model, time) = await OpenAsync();
        await using var _ = model;
        backend.QuietFetch = _ => GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        await model.Workbench.AutoFetch.TickAsync();
        Assert.EndsWith("Fetch resumes it", model.AutoFetch.FetchTip);
        time.Now = time.Now.AddHours(1);
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Single(backend.QuietFetches);

        await model.Sync.FetchCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        Assert.Equal("", model.StatusBar.Notice);
        backend.QuietFetch = _ => new(GitWriteOutcome.Succeeded, "Fetched.");
        time.Now = time.Now.AddMinutes(1);
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Equal(2, backend.QuietFetches.Count);
    }

    // A push is no fetch, so the dropped fetch is not counted as done either: the next tick fetches at once.
    [Fact]
    public async Task WhenAWriteStartsDuringAFetch_ShouldGoFirstAndNotCountTheStoppedFetchAsFailed()
    {
        var (backend, model, _) = await OpenAsync();
        await using var __ = model;
        backend.QuietGate.IsClosed = true;
        var fetching = model.Workbench.AutoFetch.TickAsync();
        Assert.True(model.Workbench.AutoFetch.IsFetching);
        backend.Gate.IsClosed = true;
        var writing = model.Sync.PushCommand.ExecuteAsync(null);
        await fetching;
        Assert.False(model.Workbench.AutoFetch.IsFetching);
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Single(backend.QuietFetches);
        backend.Gate.Open(); backend.QuietGate.Open();
        await writing; await model.Sync.SyncCompletion;

        await model.Workbench.AutoFetch.TickAsync();
        Assert.Equal(2, backend.QuietFetches.Count);
    }

    // The schedule belongs to the application and outlives the repository.
    [Fact]
    public async Task WhenAnotherRepositoryOpensDuringAFetch_ShouldDropItWithoutFailureAndFetchTheNewOneAtTheNextTick()
    {
        var (backend, model, _) = await OpenAsync();
        await using var __ = model;
        backend.QuietGate.IsClosed = true;
        var fetching = model.Workbench.AutoFetch.TickAsync();
        Assert.True(model.Workbench.AutoFetch.IsFetching);
        backend.Snapshot = Trunk with { Root = "second" };
        await model.Session.OpenRepositoryAsync("second"); await model.Sync.SyncCompletion;
        // The held fetch ends only through its cancellation; a fetch that is not dropped fails the wait instead of hanging.
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(model.Workbench.AutoFetch.IsFetching);
        backend.QuietGate.Open();

        await model.Workbench.AutoFetch.TickAsync();
        Assert.Equal(2, backend.QuietFetches.Count); Assert.Equal("second", backend.QuietFetches[^1].Expected.Root);
        Assert.Equal("", model.StatusBar.Notice);
    }

    // The tip is the Fetch button's and the choice the Settings card's; the refused sign-in stops the schedule and the
    // user's own fetch resumes it.
    [Fact]
    public async Task WhenAFetchIsRefusedResumedOrTheIntervalChanges_ShouldAnnounceTheTipAndTheChoice()
    {
        var (backend, model, _) = await OpenAsync();
        await using var __ = model;
        var changes = new List<string?>();
        model.AutoFetch.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        backend.QuietFetch = _ => GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Equal([nameof(model.AutoFetch.FetchTip)], changes);

        changes.Clear();
        await model.Sync.FetchCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        Assert.Equal([nameof(model.AutoFetch.FetchTip)], changes);

        changes.Clear();
        model.Layout.AutoFetchMinutes = 5;
        Assert.Equal([nameof(model.AutoFetch.Selected), nameof(model.AutoFetch.FetchTip)], changes);
    }

    [Fact]
    public async Task WhenTheApplicationClosesDuringAFetch_ShouldDropTheFetchInsteadOfWaitingForIt()
    {
        var (backend, model, _) = await OpenAsync();
        backend.QuietGate.IsClosed = true;
        var fetching = model.Workbench.AutoFetch.TickAsync();
        Assert.True(model.Workbench.AutoFetch.IsFetching);
        await model.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(model.Workbench.AutoFetch.IsFetching);
        backend.QuietGate.Open();
    }

    [Fact]
    public async Task WhenTheUserChoosesOff_ShouldFetchNothingAndSaveTheChoice()
    {
        var (backend, model, _) = await OpenAsync();
        await using var _ = model;
        model.AutoFetch.Selected = AutoFetchText.Options[0];
        Assert.Equal(0, model.Layout.AutoFetchMinutes);
        await model.Workbench.AutoFetch.TickAsync(); await model.Workbench.AutoFetch.ActivatedAsync();
        Assert.Empty(backend.QuietFetches);
        model.Layout.AutoFetchMinutes = 5;
        Assert.Equal("Every 5 minutes", model.AutoFetch.Selected.Label);
    }

    [Fact]
    public async Task WhenTheBranchHasNoUpstream_ShouldFetchNothing()
    {
        var (untracked, model, _) = await OpenAsync(branch: null);
        await using var _ = model;
        await model.Workbench.AutoFetch.TickAsync();
        Assert.Empty(untracked.QuietFetches);
    }
}
