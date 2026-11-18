using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Operations;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.Live;

// Owns every watch request, including a canceled loop that has not returned yet.
public sealed partial class LiveRefresh(ILiveRepositoryWorkspace? workspace, StatusBarState statusBar,
    Func<RepositorySnapshot, SnapshotChange, CancellationToken, Task> apply) : ObservableObject
{
    private readonly LatestRequest request = new();
    public Task Completion { get; private set; } = Task.CompletedTask;
    [ObservableProperty] public partial string Status { get; private set; } = "";

    public void Start()
    {
        Stop();
        if (workspace is null)
            return;
        Completion = Task.WhenAll(Completion, ObserveAsync(request.Begin()));
    }

    public void Stop()
    {
        request.Cancel();
        Status = "";
    }

    private async Task ObserveAsync(LatestRequest.Scope scope)
    {
        using (scope)
        {
            try
            {
                Status = LiveRefreshText.Watching(false);
                await foreach (var update in workspace!.WatchAsync(scope.Token))
                {
                    if (!scope.IsCurrent)
                        return;
                    if (update.Snapshot is not { } snapshot)
                    {
                        ShowRetry();
                        continue;
                    }
                    if (!workspace.IsCurrent(snapshot)) continue;
                    await apply(snapshot, update.Change, scope.Token);
                    if (!scope.IsCurrent) return;
                    ShowWatching(update.IsPolling);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!scope.IsCurrent)
                    return;
                Status = LiveRefreshText.Stopped;
                statusBar.Notice = LiveRefreshText.Unavailable;
            }
        }
    }

    private void ShowRetry()
    {
        Status = LiveRefreshText.Retrying;
        statusBar.Notice = LiveRefreshText.NotChecked;
    }

    private void ShowWatching(bool polling)
    {
        Status = LiveRefreshText.Watching(polling);
        if (statusBar.Notice == LiveRefreshText.NotChecked) statusBar.Notice = "";
    }
}
