using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Rebase;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Rebase;

// A rebase in progress: its plan and what is left of the commit Git stopped at, the message that commit is committed
// with, the abort question, Continue and Skip. What is left, what keeps Continue back and the writes are the repository's
// rebase, every sentence comes from Domain (RebaseOverview); this holds the message and the question.
public sealed partial class RebaseViewModel : ObservableObject
{
    private readonly RebaseModel rebase;
    private readonly Repository repository;

    public RebaseViewModel(RebaseModel rebase)
    {
        this.rebase = rebase;
        repository = rebase.Repository;
        Abort = new(repository, _ => rebase.AbortAsync(), _ => RebaseText.AbortQuestion, Failures.OperationNotAborted, () => repository.CanRebase);
        repository.SnapshotApplied += (_, _) => Follow();
        repository.Requery.Track(ContinueRebaseCommand, SkipCommitCommand, RequestAbortRebaseCommand);
    }

    // The question before the rebase in progress is aborted; its subject is the stop it was asked at.
    public Confirmation<string> Abort { get; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueRebaseCommand))] public partial RebaseOverview Overview { get; private set; } = RebaseOverview.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueRebaseCommand))] public partial string Message { get; set; } = "";

    private bool CanRebase => repository.CanRebase;
    private bool CanContinue() => rebase.CanContinue(Message);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private Task ContinueRebaseAsync() => rebase.ContinueAsync(Message);
    [RelayCommand(CanExecute = nameof(CanRebase))]
    private Task SkipCommitAsync() => rebase.SkipAsync();

    [RelayCommand(CanExecute = nameof(CanRebase))]
    private void RequestAbortRebase()
    {
        if (repository.Snapshot?.RebaseStopIdentity is { } asked) Abort.Request(asked);
    }

    // Every stop brings the stopped commit's message without Git's conflict list and closes the abort question; the same
    // stop seen again keeps the draft.
    private void Follow()
    {
        if (rebase.IsNewStop)
        {
            Message = rebase.Message;
            Abort.Close();
        }
        Overview = rebase.Overview;
    }
}
