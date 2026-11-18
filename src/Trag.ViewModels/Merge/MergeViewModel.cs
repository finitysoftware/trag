using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Merge;

// A merge in progress: what is left of it, the message it is finished with, the abort question, and Finish. What is left,
// what keeps Finish back and the writes are the repository's merge; this holds the message and the question.
public sealed partial class MergeViewModel : ObservableObject
{
    private readonly MergeModel merge;
    private readonly Repository repository;

    public MergeViewModel(MergeModel merge)
    {
        this.merge = merge;
        repository = merge.Repository;
        Abort = new(repository, _ => merge.AbortAsync(), _ => MergeText.AbortQuestion, Failures.OperationNotAborted, () => repository.CanMerge);
        repository.SnapshotApplied += (_, _) => Follow();
        repository.Requery.Track(FinishMergeCommand, RequestAbortMergeCommand);
    }

    // The question before the merge in progress is aborted; its subject is the merge it was asked about.
    public Confirmation<string> Abort { get; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(FinishMergeCommand))] public partial MergeOverview Overview { get; private set; } = MergeOverview.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(FinishMergeCommand))] public partial string Message { get; set; } = "";

    private bool CanMerge => repository.CanMerge;
    private bool CanFinishMerge() => merge.CanFinish(Message);

    [RelayCommand(CanExecute = nameof(CanFinishMerge))]
    private Task FinishMergeAsync() => merge.FinishAsync(Message);

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void RequestAbortMerge()
    {
        if (repository.Snapshot?.MergeIdentity is { } asked) Abort.Request(asked);
    }

    // A new merge takes Git's message without its conflict list and closes the abort question; the same merge seen again
    // keeps the draft.
    private void Follow()
    {
        if (merge.IsNew)
        {
            Message = merge.Message;
            Abort.Close();
        }
        Overview = merge.Overview;
    }
}
