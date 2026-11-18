using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Operations;
using Trag.Application.Ports.Storage;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.Live;

// Opens, refreshes and closes the session: the workspace read, the request that tells a late result it is stale, the
// watcher around it, the remembered location, and what a failed or canceled read says. Every open and every close starts
// a new Repository, empty until a read shows it, in place of the current one, which goes with everything that followed
// it; present hands each new one to the composition, which composes its features. Every snapshot goes through the
// repository's status, which has its file lists ready before the repository publishes it, so every follower of
// SnapshotApplied finds them there.
public sealed partial class RepositorySession : ObservableObject
{
    private readonly RepositoryServices services;
    private readonly IRepositoryWorkspace? workspace;
    private readonly ILastRepositoryStore settings;
    private readonly StatusBarState statusBar;
    private readonly Action<Repository> present;
    private readonly LatestRequest request = new();
    private string? requestedPath;
    private string? countedLine;
    // The repository list records every open; the workbench supplies it once the catalog exists.
    public Func<RepositorySnapshot, CancellationToken, Task> RecordOpened { get; set; } = (_, _) => Task.CompletedTask;
    // Raised while the repository closes, after an empty one took its place and before the workspace is released.
    public event Action? Closed;
    // The application's commands whose CanExecute reads the open repository: they are asked again after every pass of
    // the repository's own requery, and whenever application state they read changes.
    public Requery Requery { get; } = new();

    public RepositorySession(RepositoryServices services, ILastRepositoryStore settings, Action<Repository> present, CancelRegistry cancels)
    {
        this.services = services;
        (workspace, statusBar) = (services.Workspace, services.StatusBar);
        this.settings = settings;
        this.present = present;
        Watcher = new(workspace as ILiveRepositoryWorkspace, statusBar, (snapshot, change, ct) => UpdateSnapshotAsync(snapshot, ct, change));
        cancels.Register(() => false, request.Interrupt);
        Current = Follow(new Repository(services));
        present(Current);
    }

    // The repository shown now: never none, only one without a snapshot.
    [ObservableProperty] public partial Repository Current { get; private set; }
    // Refresh and Close depend on an open repository; opening and closing ask every tracked command again.
    public bool HasSession => requestedPath is not null;
    public LiveRefresh Watcher { get; }

    // Restores the last repository at startup; the shell shows the picker when nothing is open afterwards.
    public async Task RestoreAsync(CancellationToken ct)
    {
        var ticket = request.Generation;
        try
        {
            var path = await settings.LoadAsync(ct);
            if (request.IsCurrent(ticket) && path is not null)
                await OpenRepositoryAsync(path, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (request.IsCurrent(ticket)) statusBar.Notice = SessionText.NotRestored; }
    }
    public Task OpenRepositoryAsync(string path, CancellationToken ct = default) => LoadAsync(path, false, ct);
    public bool CanRefresh => HasSession && !Current.IsBusy;
    // A refresh with nothing shown yet, after an open that was canceled or failed, is that open again.
    public Task RefreshAsync() => requestedPath is null ? Task.CompletedTask : LoadAsync(requestedPath, Current.HasRepository, CancellationToken.None);
    public bool CanClose => HasSession && !Current.IsWriting;

    private async Task LoadAsync(string path, bool refresh, CancellationToken ct)
    {
        if (workspace is null || Current.IsWriting)
            return;
        Watcher.Stop();
        using var scope = request.Begin(ct);
        PrepareLoad(path, refresh);
        try
        {
            // No snapshot is a read the workspace dropped for a newer one: this load was stopped.
            var snapshot = (refresh ? await workspace.RefreshAsync(scope.Token) : await workspace.OpenAsync(path, scope.Token))
                ?? throw new OperationCanceledException();
            scope.Token.ThrowIfCancellationRequested();
            await Current.Status.ShowAsync(snapshot, refresh && Current.HasRepository, scope.Token, () => Publish(snapshot), SnapshotChange.Everything);
            if (refresh)
            {
                // The refresh answered whatever the last write left open.
                statusBar.WriteDetails = "";
                Current.CompleteRefresh();
            }
            else await RecordOpenAsync(snapshot, scope);
        }
        catch (OperationCanceledException) { ShowCanceledLoad(scope); }
        catch (Exception error) { ShowFailedLoad(scope, error); }
        finally { FinishLoad(scope); }
    }

    private void PrepareLoad(string path, bool refresh)
    {
        if (!refresh) Clear();
        requestedPath = path;
        Current.IsReading = true;
        statusBar.ErrorMessage = "";
        statusBar.Notice = "";
        statusBar.Status = SessionText.Reading(refresh);
    }

    private void ShowCanceledLoad(LatestRequest.Scope scope)
    {
        if (!scope.IsLatest) return;
        statusBar.Status = SessionText.Canceled(Current.HasRepository);
    }

    private void ShowFailedLoad(LatestRequest.Scope scope, Exception error)
    {
        if (!scope.IsLatest) return;
        statusBar.ErrorMessage = SessionText.Error((error as RepositoryException)?.Error);
        statusBar.Status = SessionText.Failed(Current.HasRepository);
    }

    private void FinishLoad(LatestRequest.Scope scope)
    {
        if (!scope.IsLatest) return;
        Current.IsReading = false;
        if (Current.HasRepository) Watcher.Start();
    }

    // The open is shown by now: Esc can still stop remembering and recording it, but not take the open back.
    private async Task RecordOpenAsync(RepositorySnapshot snapshot, LatestRequest.Scope scope)
    {
        try
        {
            await settings.SaveAsync(snapshot.Root, scope.Token);
        }
        catch (Exception)
        {
            if (scope.IsLatest)
                statusBar.Notice = SessionText.NotRemembered;
        }
        if (scope.IsCurrent)
            await RecordOpened(snapshot, scope.Token);
    }

    // The writer's verified snapshot and the watcher's updates arrive here: a delta over the shown lists, then the publication.
    // Only the watcher knows which files changed; a write may have changed any.
    public Task UpdateSnapshotAsync(RepositorySnapshot snapshot, CancellationToken ct, SnapshotChange? change = null) =>
        Current.Status.ShowAsync(snapshot, true, ct, () => Publish(snapshot, live: true), change ?? SnapshotChange.Everything);

    // The snapshot becomes the shown one, the status bar warns about what the repository's state holds back, and the
    // status line counts its sides. A live update counts again only when the count changed, so a write's result or a
    // refusal stays readable until then instead of the watcher's next echo.
    private void Publish(RepositorySnapshot snapshot, bool live = false)
    {
        Current.Apply(snapshot);
        statusBar.RepositoryWarning = Current.Header.Warning;
        var line = StatusText.Line(snapshot);
        if (!live || line != countedLine) statusBar.Status = line;
        countedLine = line;
    }

    public async Task CloseRepositoryAsync()
    {
        if (Current.IsWriting)
            return;
        Watcher.Stop();
        using var scope = request.Begin();
        requestedPath = null;
        Current.IsReading = false;
        Clear();
        statusBar.Status = SessionText.Closed;
        Closed?.Invoke();
        try
        {
            if (workspace is not null)
                await workspace.CloseAsync();
            if (scope.IsLatest)
                await settings.SaveAsync(null, scope.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (scope.IsLatest) statusBar.Notice = SessionText.NotForgotten; }
    }
    // A new, empty repository takes the shown one's place, and the texts about the one that goes are cleared.
    private void Clear()
    {
        var previous = Current;
        previous.Requery.Suggested -= Requery.Invalidate;
        previous.PropertyChanged -= FollowWriting;
        previous.Cancels.CancelAll();
        var next = Follow(new Repository(services));
        present(next);
        Current = next;
        statusBar.ErrorMessage = statusBar.Notice = statusBar.RepositoryWarning = statusBar.WriteDetails = "";
        statusBar.SyncOperationText = statusBar.SyncDetails = "";
        Requery.Invalidate();
    }

    // The application's commands follow the repository's requery; a write pauses the watcher, and the snapshot it
    // verified is shown here like every other.
    private Repository Follow(Repository repository)
    {
        repository.Writer.ApplySnapshotAsync = snapshot => UpdateSnapshotAsync(snapshot, CancellationToken.None);
        repository.Requery.Suggested += Requery.Invalidate;
        repository.PropertyChanged += FollowWriting;
        return repository;
    }

    // A write pauses the watcher; its end resumes it for the session the write belonged to.
    private void FollowWriting(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Repository.IsWriting)) return;
        if (Current.IsWriting) Watcher.Stop();
        else if (Current.Writer.WriteBelongsToSession && Current.HasRepository) Watcher.Start();
    }

    // Stops the watcher, the open in flight, which ends quietly, and what the shown repository still writes; the workspace
    // itself is released after the features let go.
    public void Stop()
    {
        Watcher.Stop();
        request.Cancel();
        Current.Retire();
    }
    public async ValueTask DisposeAsync()
    {
        Stop();
        await Watcher.Completion;
        if (workspace is not null)
            await workspace.DisposeAsync();
    }
}
