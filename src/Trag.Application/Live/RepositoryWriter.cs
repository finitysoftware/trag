using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Operations;
using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Live;

// Every Git write of the shell goes through here: one at a time, only when the repository allows its kind, with the shown
// snapshot replaced by the one the write verified. The repository state carries the flags (IsWriting, NeedsReview);
// features follow Running and LastWrite; the writer owns what is the same for staging, committing, branches, stashes,
// merges, publishing and sync, the status bar's report of how each ended included.
public sealed partial class RepositoryWriter(IRepositoryWorkspace? writable, StatusBarState statusBar, Repository repository, IViewDispatcher dispatcher,
    Action<Exception> defects) : ObservableObject
{
    // Holds the token of the write in flight; a write's result belongs to the session for as long as its repository does.
    private readonly LatestRequest inFlight = new();
    // Applies a verified snapshot to the file lists before the repository state; the session feature supplies it.
    public Func<RepositorySnapshot, Task> ApplySnapshotAsync { get; set; } = _ => Task.CompletedTask;

    // The request in flight, from the moment the generation advanced until after IsWriting is reset, so what follows
    // a write still knows which write it was.
    [ObservableProperty] public partial GitWriteRequest? Running { get; private set; }
    // The outcome of the last write that still belonged to the current session, set after its snapshot was applied.
    [ObservableProperty] public partial WriteOutcome? LastWrite { get; private set; }
    // The write running is a sync, which reports in its own panel, or part of one file's changes, which keeps that file's
    // diff on screen: the rest of the file replaces it in place.
    public bool RunsSync => Running is SyncRequest;
    public bool RunsPartOfAFile => Running is PatchRequest;
    // Whether the write in flight, or the one that just ended, still belongs to the session: its repository was not
    // retired since it began. What follows the end of a write asks it when IsWriting is reset.
    public bool WriteBelongsToSession => !repository.IsRetired;

    // A write whose request was built over the shown snapshot already, as a confirmed publish. A sync and a publish put
    // Git's progress on the status line while they run, and hand it to progress too.
    public async Task<GitWriteResult?> RunAsync(GitWriteRequest request, Action<string>? progress = null)
    {
        if (writable is null || repository.IsWriting) return null;
        if (Refusal(request) is { } refusal)
        {
            statusBar.Status = refusal;
            return null;
        }
        Running = request;
        var reportsSync = WriteText.ReportsInSyncPanel(request);
        using var scope = inFlight.Begin();
        repository.IsWriting = true;
        try
        {
            Prepare(request, reportsSync);
            if (request is SyncRequest or PublishRequest) request = request with { Progress = StatusProgress(progress) };
            var result = await writable.WriteAsync(request, scope.Token);
            return await CompleteAsync(request, result, reportsSync);
        }
        catch (Exception error)
        {
            defects(error);
            if (!repository.IsRetired)
            {
                repository.NeedsReview = true;
                ShowUndisplayed(request);
                LastWrite = new(request, null);
            }
        }
        finally
        {
            repository.IsWriting = false;
            Running = null;
        }
        return null;
    }

    // Cancels the write in flight; how it ended is still verified and reported.
    public void Cancel() => inFlight.Cancel();

    // The status line says what the write does, after the texts of the previous one were cleared: a sync keeps the last
    // write's details, and an outcome waiting for review keeps its banner until F5 answers it.
    private void Prepare(GitWriteRequest request, bool reportsSync)
    {
        if (!repository.NeedsReview) statusBar.ErrorMessage = "";
        if (request is SyncRequest) statusBar.SyncDetails = "";
        else statusBar.WriteDetails = statusBar.SyncOperationText = statusBar.SyncDetails = "";
        statusBar.Status = WriteText.Progress(request);
        if (reportsSync) statusBar.SyncOperationText = statusBar.Status;
    }

    private async Task<GitWriteResult?> CompleteAsync(GitWriteRequest request, GitWriteResult result, bool reportsSync)
    {
        if (repository.IsRetired) return null;
        if (result.Snapshot is { } snapshot) await ApplySnapshotAsync(snapshot);
        if (repository.IsRetired) return null;
        result = SigningWrites.Described(request, ResolutionOutcome.Described(request, result));
        // Only F5 answers an outcome waiting for review; the account choice it lets through verifies nothing of it.
        if (result.Outcome == GitWriteOutcome.Uncertain) repository.NeedsReview = true;
        statusBar.Status = result.Message;
        if (reportsSync)
        {
            statusBar.SyncOperationText = result.Message;
            statusBar.SyncDetails = result.Details;
        }
        // A sync reports in its panel alone; every other write leaves Git's output under Details and a refusal or an
        // unverified outcome in the error banner.
        if (request is not SyncRequest)
        {
            statusBar.WriteDetails = result.Details;
            if (result.Outcome is GitWriteOutcome.Rejected or GitWriteOutcome.Uncertain) statusBar.ErrorMessage = result.Message;
        }
        LastWrite = new(request, result);
        return result;
    }

    // The write threw before Git could report, so its outcome is unknown: a sync says so in its panel, every other write
    // in the error banner.
    private void ShowUndisplayed(GitWriteRequest request)
    {
        if (request is SyncRequest)
        {
            statusBar.SyncOperationText = SyncText.ResultNotDisplayed;
            statusBar.Status = statusBar.SyncOperationText;
            return;
        }
        statusBar.ErrorMessage = CommitText.ResultNotDisplayed;
        statusBar.Status = statusBar.ErrorMessage;
    }

    // What a write needs from the repository besides no other write running (Needs); choosing the account a remote uses
    // needs nothing more. Commands ask the same state, but a call no command held back, as a branch picker row, which
    // stays enabled for its menu, or one whose state changed while its command awaited, is refused here and says why.
    private string? Refusal(GitWriteRequest request) =>
        WriteNeeds.Of(request) == WriteNeed.Nothing ? null
        : repository.NeedsReview ? WriteText.ReviewFirst
        : repository.IsReading ? WriteText.ReadInProgress
        : repository.Snapshot is { } snapshot && WriteNeeds.StateAllows(request, snapshot) ? null
        : WriteText.StateUnsupported;

    // A write that did not start: the caller's own check refused it.
    public static readonly Task<GitWriteResult?> NotRun = Task.FromResult<GitWriteResult?>(null);

    // Every feature's write starts the same way: the caller has checked what is its own, and the request is built over the
    // shown snapshot. Without a snapshot there is nothing to write to.
    public Task<GitWriteResult?> RunAsync(Func<RepositorySnapshot, GitWriteRequest> build) =>
        repository.Snapshot is { } expected ? RunAsync(build(expected)) : NotRun;

    // Every value Git reports becomes the status line while the write is still running; one that arrives after it ended
    // would replace how it ended.
    private IProgress<string> StatusProgress(Action<string>? also) =>
        ProgressDelivery.Of<string>(value => dispatcher.InvokeAsync(() =>
        {
            if (Running is null) return;
            statusBar.SyncOperationText = statusBar.Status = value;
            also?.Invoke(value);
        }));
}
