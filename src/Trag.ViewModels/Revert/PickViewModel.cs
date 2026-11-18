using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Revert;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Revert;

// A commit Git applies to the current branch, a revert or a cherry-pick, stopped at its conflicts: finished once the last
// one is resolved, or aborted after a question, every word named for the operation. The writes are the repository's
// pick's (PickModel).
public sealed partial class PickViewModel : ObservableObject
{
    private readonly PickModel pick;
    private readonly Repository repository;

    public PickViewModel(PickModel pick)
    {
        this.pick = pick;
        repository = pick.Repository;
        Abort = new(repository, _ => pick.AbortAsync(), _ => Words.AbortQuestion, Failures.OperationNotAborted, () => repository.CanFinishPick);
        repository.SnapshotApplied += (_, _) => Follow();
        repository.Requery.Track(FinishPickCommand, RequestAbortPickCommand);
    }

    // The question before the pick in progress is aborted; its subject is the commit Git applies, and it goes with that
    // pick, so a later one never opens with it.
    public Confirmation<string> Abort { get; }
    public PickWords Words => pick.Words;

    [RelayCommand(CanExecute = nameof(CanFinishPick))]
    private Task FinishPickAsync() => pick.FinishAsync();
    private bool CanFinishPick() => pick.CanFinish;

    [RelayCommand(CanExecute = nameof(CanRequestAbort))]
    private void RequestAbortPick()
    {
        if (repository.Snapshot?.PickHead is { } picked) Abort.Request(picked);
    }
    private bool CanRequestAbort() => repository.CanFinishPick;

    private void Follow()
    {
        if (Abort.Subject is { } asked && repository.Snapshot?.PickHead != asked) Abort.Close();
        OnPropertyChanged(string.Empty);
    }
}
