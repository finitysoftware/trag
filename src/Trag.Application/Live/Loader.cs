using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Operations;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.Live;

// One list or record a feature reads, with the mechanics every such read repeats: the latest request wins, a snapshot
// that moved under the read says so, a failure becomes a message, and Completion lets a test wait. With a key it
// follows the repository: it reads when the key of the applied snapshot changes, again when a read of it did not answer
// (it moved or was dropped under the read) and the next snapshot or a screen shows the value, and empties when the key
// is gone.
// LoadAsync reads on demand with any read, snapshot or not. It empties on Clear; over a repository it goes with that
// repository. Registered for cancelling, the shell's Cancel reaches the read in flight, until Release for a loader that
// goes before its registry does.
public sealed partial class Loader<T> : ObservableObject where T : class
{
    private readonly Repository? repository;
    private readonly Func<RepositorySnapshot, CancellationToken, Task<T?>>? read;
    private readonly Func<RepositorySnapshot, object?>? key;
    private readonly CancelRegistry? cancels;
    private readonly IDisposable? registration;
    private readonly T empty;
    private readonly LatestRequest request = new();
    private object? lastKey;

    public Loader(Repository repository, Func<RepositorySnapshot, CancellationToken, Task<T?>>? read, T empty,
        Func<RepositorySnapshot, object?>? key = null, CancelRegistry? cancels = null) : this(empty, cancels)
    {
        this.repository = repository;
        this.read = read;
        this.key = key;
        repository.SnapshotApplied += (snapshot, _) => Follow(snapshot);
    }

    public Loader(T empty, CancelRegistry? cancels = null)
    {
        this.empty = empty;
        this.cancels = cancels;
        Value = empty;
        registration = cancels?.Register(() => IsBusy, Interrupt);
    }

    [ObservableProperty] public partial T Value { get; private set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Message { get; private set; } = "";
    public Task Completion { get; private set; } = Task.CompletedTask;
    // What the message says when the repository moved under the read, when the read failed, and when Cancel interrupted
    // it (nothing, unless the owner wants a word). DescribeFailure lets a domain exception speak for itself; Git that did
    // not answer in time says so in every feature's words alike.
    public string ChangedMessage { get; init; } = Failures.ListChanged;
    public string FailedMessage { get; init; } = Failures.ListNotRead;
    public string CanceledMessage { get; init; } = "";
    public Func<Exception, string?>? DescribeFailure { get; init; }

    partial void OnIsBusyChanged(bool value) => cancels?.NotifyChanged();

    // Reads the constructor's read over the shown snapshot; a read still in flight is superseded.
    public Task ReloadAsync()
    {
        if (repository?.Snapshot is not { } snapshot || read is null)
        {
            Value = empty;
            return Completion;
        }
        return LoadAsync(ct => read(snapshot, ct));
    }

    // Reads on demand; interim is shown until the read answers, and stays if it does not.
    public Task LoadAsync(Func<CancellationToken, Task<T?>> read, T? interim = null) => Completion = ReadAsync(read, interim);

    // Drops the read in flight and keeps the value.
    public void Cancel()
    {
        request.Cancel();
        IsBusy = false;
    }

    // Nothing to show: the value empties and a read in flight is dropped.
    public void Clear()
    {
        Cancel();
        Message = "";
        Value = empty;
        lastKey = null;
    }

    // The shell's Cancel: the read is interrupted but stays the latest, so it can report the interruption.
    private void Interrupt()
    {
        request.Interrupt();
        IsBusy = false;
    }

    // The loader goes while its registry stays: the shell's Cancel reaches it no more.
    public void Release() => registration?.Dispose();

    // A screen shows the value: the shown snapshot's read runs now if it is still owed, since the last one did not answer.
    public Task ShowAsync()
    {
        if (repository?.Snapshot is { } snapshot) Follow(snapshot);
        return Completion;
    }

    private void Follow(RepositorySnapshot snapshot)
    {
        if (key is null) return;
        var next = key(snapshot);
        if (Equals(next, lastKey)) return;
        lastKey = next;
        if (next is null) Clear();
        else ReloadAsync();
    }

    private async Task ReadAsync(Func<CancellationToken, Task<T?>> read, T? interim)
    {
        using var scope = request.Begin();
        IsBusy = true;
        Message = "";
        if (interim is not null) Value = interim;
        try
        {
            var result = await read(scope.Token);
            if (!scope.IsCurrent) return;
            if (result is null)
            {
                Message = ChangedMessage;
                lastKey = null;
                return;
            }
            Value = result;
        }
        catch (OperationCanceledException)
        {
            if (scope.IsLatest)
            {
                lastKey = null;
                if (CanceledMessage.Length != 0) Message = CanceledMessage;
            }
        }
        catch (Exception error)
        {
            // A read that fails after it was interrupted reports the interruption, not the failure it ran into.
            if (scope.IsCurrent) Message = error is TimeoutException ? Failures.TimedOut : DescribeFailure?.Invoke(error) ?? FailedMessage;
            else if (scope.IsLatest && CanceledMessage.Length != 0) Message = CanceledMessage;
        }
        finally { if (scope.IsLatest) IsBusy = false; }
    }
}
