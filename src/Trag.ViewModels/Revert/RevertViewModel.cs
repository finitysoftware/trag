using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Revert;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Revert;

// Reverting the commit chosen in History: a question first, which says what keeps the revert back, if anything. A revert
// stopped at conflicts has started, so the question closes then too, and the pick in progress takes over
// (PickViewModel); any other answer stays in it. What the question says and the write are the repository's reverts'
// (RevertModel).
public sealed partial class RevertViewModel : ObservableObject
{
    private readonly RevertModel reverts;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public RevertViewModel(RevertModel reverts, HistoryModel history)
    {
        this.reverts = reverts;
        repository = reverts.Repository;
        this.history = history;
        Question = new(repository, reverts.RevertAsync, _ => reverts.Question, Failures.CommitNotReverted, () => reverts.CanRevert);
        Question.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Confirmation<HistoryCommit>.Subject)) FollowQuestion(); };
        repository.SnapshotApplied += (_, _) => FollowQuestion();
        repository.Requery.Track(RequestRevertCommand);
    }

    public Confirmation<HistoryCommit> Question { get; }
    // The question's title, and what keeps the revert back; empty when nothing does.
    public string Title => reverts.Title;
    public string Refusal => reverts.Refusal;
    public bool HasRefusal => Refusal.Length != 0;

    private void FollowQuestion()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Refusal));
        OnPropertyChanged(nameof(HasRefusal));
    }

    private bool CanRequestRevert() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanRequestRevert))]
    private void RequestRevert()
    {
        if (history.Selected is not { } commit) return;
        reverts.Ask(commit);
        Question.Request(commit);
    }
}
