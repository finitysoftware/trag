using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.CherryPick;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.CherryPick;

// Cherry-picking the commit chosen in History: a question first, which says what keeps the cherry-pick back, if
// anything, the current branch having the commit already among it. A cherry-pick stopped at conflicts has started, so
// the question closes then too, and the pick in progress takes over (PickViewModel); any other answer stays in it. What
// the question says and the write are the repository's cherry-picks' (CherryPickModel).
public sealed partial class CherryPickViewModel : ObservableObject
{
    private readonly CherryPickModel picks;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public CherryPickViewModel(CherryPickModel picks, HistoryModel history)
    {
        this.picks = picks;
        repository = picks.Repository;
        this.history = history;
        Question = new(repository, picks.CherryPickAsync, _ => picks.Question, Failures.CommitNotCherryPicked, () => picks.CanCherryPick);
        Question.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Confirmation<HistoryCommit>.Subject)) FollowQuestion(); };
        repository.SnapshotApplied += (_, _) => FollowQuestion();
        repository.Requery.Track(RequestCherryPickCommand);
    }

    public Confirmation<HistoryCommit> Question { get; }
    // The question's title, and what keeps the cherry-pick back; empty when nothing does.
    public string Title => picks.Title;
    public string Refusal => picks.Refusal;
    public bool HasRefusal => Refusal.Length != 0;

    private void FollowQuestion()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Refusal));
        OnPropertyChanged(nameof(HasRefusal));
    }

    private bool CanRequestCherryPick() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanRequestCherryPick))]
    private async Task RequestCherryPickAsync()
    {
        if (history.Selected is not { } commit) return;
        await picks.AskAsync(commit);
        Question.Request(commit);
    }
}
