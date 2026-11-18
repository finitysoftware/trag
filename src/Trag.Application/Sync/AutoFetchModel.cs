using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.Application.Sync;

// The fetch Trag runs on its own. The schedule decides when; this runs it beside the shown state, for the target the
// sidebar shows, gives way to any write and to another repository, and tells the user only what needs them: a refused
// sign-in. The interval is a saved preference, and any fetch that succeeded, the user's own included, counts as the last
// one: the repository's remotes record every one and say when it was.
public sealed class AutoFetchModel : ObservableObject
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);
    private readonly LayoutPreferences preferences;
    private readonly StatusBarState statusBar;
    private AutoFetchSchedule schedule = new();
    private CancellationTokenSource? running;
    private CancellationTokenSource? loop;
    private bool gaveWay;
    // The repository shown now; the composition hands over every new one.
    private Repository? repository;

    public AutoFetchModel(LayoutPreferences preferences, StatusBarState statusBar, TimeProvider time)
    {
        this.preferences = preferences;
        this.statusBar = statusBar;
        Time = time;
        FollowPreference();
        preferences.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LayoutPreferences.AutoFetchMinutes)) FollowPreference(); };
    }

    // Another repository is shown: a fetch of the previous one gives way, and the schedule follows the new one's remotes.
    // Its stop, failures and last fetch were the previous one's, so only the chosen interval carries over.
    public void Follow(Repository next)
    {
        GiveWay();
        var stopped = schedule.IsStopped;
        schedule = new() { Interval = schedule.Interval };
        if (stopped) OnPropertyChanged(nameof(IsStopped));
        if (repository is not null)
        {
            repository.PropertyChanged -= FollowWriting;
            repository.Writer.PropertyChanged -= FollowWritten;
            repository.Remotes.Fetched -= FollowFetched;
        }
        repository = next;
        next.PropertyChanged += FollowWriting;
        next.Writer.PropertyChanged += FollowWritten;
        next.Remotes.Fetched += FollowFetched;
    }

    private void FollowWriting(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Repository.IsWriting) && repository!.IsWriting) GiveWay();
    }

    // A push the remote accepted proves the sign-in as a fetch would, and a changed account for the remote is a new
    // sign-in to try: either ends a stop, so the notice does not outlive what it asked for.
    private void FollowWritten(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RepositoryWriter.LastWrite) || !schedule.IsStopped) return;
        if (repository!.Writer.LastWrite is { Request: SyncRequest { Action: SyncAction.Push or SyncAction.ForcePush or SyncAction.SelectAccount },
            Result.Outcome: GitWriteOutcome.Succeeded }) Resumed(schedule.Resume);
    }

    // A fetch of the shown target succeeded, the user's own or this one: it is the last fetch from then on.
    private void FollowFetched(DateTimeOffset at) => Succeeded(at);

    public TimeProvider Time { get; }

    // The chosen interval; a choice is saved in the preferences, and the schedule follows them.
    public AutoFetchInterval Interval
    {
        get => schedule.Interval;
        set => preferences.AutoFetchMinutes = (int)value;
    }

    // A refused sign-in stopped the schedule until a fetch succeeds, a push is accepted or the account for the remote changes.
    public bool IsStopped => schedule.IsStopped;
    public bool IsFetching => running is not null;

    // The schedule runs for the application's lifetime: the composition starts it, and the tests tick it themselves.
    public void Run()
    {
        if (loop is not null) return;
        loop = new();
        _ = LoopAsync(loop.Token);
    }

    public void Stop()
    {
        loop?.Cancel();
        GiveWay();
    }

    public Task TickAsync() => schedule.IsDue(Time.GetUtcNow()) ? FetchAsync() : Task.CompletedTask;

    // The window came back: what others pushed meanwhile is fetched before the user looks for it, while the schedule runs.
    public Task ActivatedAsync() => loop is not null && schedule.IsDueOnActivation(Time.GetUtcNow()) ? FetchAsync() : Task.CompletedTask;

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Tick, Time, ct);
                await TickAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    // Only what the repository's remotes can fetch quietly; a fetch already running is not started twice.
    private Task FetchAsync()
    {
        if (running is not null || repository?.Remotes is not { CanFetchQuietly: true } remotes) return Task.CompletedTask;
        return RunAsync(remotes);
    }

    private async Task RunAsync(RemotesModel remotes)
    {
        using var cancel = new CancellationTokenSource(Limit, Time);
        running = cancel;
        gaveWay = false;
        OnPropertyChanged(nameof(IsFetching));
        try
        {
            var result = await remotes.FetchQuietlyAsync(cancel.Token);
            if (result is null || gaveWay) return;
            if (result.Outcome == GitWriteOutcome.Succeeded)
            {
                // The remotes recorded the fetch (Fetched); the same target is read again for its counts.
                remotes.Reread();
            }
            else Failed(result.RefusedCredentials);
        }
        // Cancelled by its limit, or ended in a way nothing expected: a failed fetch unless it gave way, and the schedule goes on.
        catch (Exception) { if (!gaveWay) Failed(false); }
        finally
        {
            running = null;
            OnPropertyChanged(nameof(IsFetching));
        }
    }

    // A write, or another repository, takes precedence: the fetch running is dropped and does not count as failed.
    private void GiveWay()
    {
        if (running is null) return;
        gaveWay = true;
        running.Cancel();
    }

    private void Succeeded(DateTimeOffset at) => Resumed(() => schedule.Succeeded(at));

    // Whatever ends a stop also takes down the notice that announced it.
    private void Resumed(Action resume)
    {
        var stopped = schedule.IsStopped;
        resume();
        if (!stopped || schedule.IsStopped) return;
        if (statusBar.Notice == AutoFetchText.Stopped) statusBar.Notice = "";
        OnPropertyChanged(nameof(IsStopped));
    }

    private void Failed(bool refusedCredentials)
    {
        schedule.Failed(Time.GetUtcNow(), refusedCredentials);
        if (!schedule.IsStopped) return;
        statusBar.Notice = AutoFetchText.Stopped;
        OnPropertyChanged(nameof(IsStopped));
    }

    private void FollowPreference()
    {
        schedule.Interval = AutoFetchIntervals.From(preferences.AutoFetchMinutes);
        OnPropertyChanged(nameof(Interval));
    }
}
