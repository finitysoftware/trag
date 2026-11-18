using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Reset;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Reset;

// Resetting the current branch to the commit chosen in History: a question that says how far the branch goes back,
// with the mode chosen in it (mixed unless the user picks another), and what keeps the reset back, if anything. The
// write is the repository's resets' (ResetModel).
public sealed partial class ResetViewModel : ObservableObject
{
    private readonly ResetModel resets;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public ResetViewModel(ResetModel resets, HistoryModel history)
    {
        this.resets = resets;
        repository = resets.Repository;
        this.history = history;
        Question = new(repository, commit => resets.ResetAsync(commit, Mode), _ => resets.Summary,
            Failures.BranchNotReset, () => resets.CanReset);
        Question.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Confirmation<HistoryCommit>.Subject)) FollowQuestion(); };
        repository.SnapshotApplied += (_, _) => FollowQuestion();
        repository.Requery.Track(RequestResetCommand);
    }

    public Confirmation<HistoryCommit> Question { get; }
    // Said while the question is open.
    public string Title => Question.Subject is null ? "" : resets.Title;
    public string Refusal => Question.Subject is null ? "" : resets.Refusal;
    public bool HasRefusal => Refusal.Length != 0;
    public string PushedWarning => Question.Subject is null ? "" : resets.Pushed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSoft), nameof(IsMixed), nameof(IsHard), nameof(Loss))]
    public partial ResetMode Mode { get; set; } = ResetMode.Mixed;

    // The three choices as the radio buttons set them.
    public bool IsSoft { get => Mode == ResetMode.Soft; set { if (value) Mode = ResetMode.Soft; } }
    public bool IsMixed { get => Mode == ResetMode.Mixed; set { if (value) Mode = ResetMode.Mixed; } }
    public bool IsHard { get => Mode == ResetMode.Hard; set { if (value) Mode = ResetMode.Hard; } }
    // What a hard reset takes with it; empty for the others.
    public string Loss => IsHard ? resets.Loss : "";

    private void FollowQuestion()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Refusal));
        OnPropertyChanged(nameof(HasRefusal));
        OnPropertyChanged(nameof(PushedWarning));
        OnPropertyChanged(nameof(Loss));
    }

    private bool CanRequestReset() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanRequestReset))]
    private async Task RequestResetAsync()
    {
        if (history.Selected is not { } commit) return;
        await resets.AskAsync(commit);
        Mode = ResetMode.Mixed;
        Question.Request(commit);
    }
}
