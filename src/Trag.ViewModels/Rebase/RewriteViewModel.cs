using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Rebase;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Rebase;

// Editing the current branch's commits from the one chosen in History: those commits, oldest first, each with what
// happens to it and moved up or down, and the message the commit a row's group becomes, edited where the plan sets one.
// What keeps the edit back and what the upstream already has are said before Git is asked; the write is the repository's
// rewrites' (RewriteModel). When Git stops on the way, the dialog closes and the rebase view takes over.
public sealed partial class RewriteViewModel : ObservableObject
{
    private readonly RewriteModel rewrites;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public RewriteViewModel(RewriteModel rewrites, HistoryModel history)
    {
        this.rewrites = rewrites;
        repository = rewrites.Repository;
        this.history = history;
        Question = new(repository, _ => rewrites.RewriteAsync(), _ => rewrites.Subtitle, Failures.CommitsNotEdited, () => rewrites.CanRewrite);
        repository.SnapshotApplied += (_, _) => { OnPropertyChanged(nameof(Title)); PlanChanged(); };
        repository.Requery.Track(RequestRewriteCommand);
    }

    public Confirmation<HistoryCommit> Question { get; }
    public ObservableCollection<RewriteRow> Rows { get; } = [];
    public IReadOnlyList<RewriteChoice> Actions => RewriteText.Choices;
    [ObservableProperty] public partial RewriteRow? SelectedRow { get; set; }
    public string Warning => rewrites.Warning;
    public string Title => rewrites.Title;
    public string Refusal => rewrites.Refusal;

    // The message the selected row's group becomes, edited where the plan sets one: a reword's, or a squash's joined one.
    public bool CanEditMessage => Selected() is { } index && rewrites.Draft.CanEditMessage(index);
    public string Message
    {
        get => Selected() is { } index ? rewrites.Draft.MessageAt(index) : "";
        set
        {
            if (Selected() is not { } index) return;
            rewrites.Edit(index, value);
            PlanChanged();
        }
    }

    private bool CanRequestRewrite() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanRequestRewrite))]
    private async Task RequestRewriteAsync()
    {
        if (history.Selected is not { } commit) return;
        await rewrites.OpenAsync(commit);
        Rows.Clear();
        foreach (var step in rewrites.Draft.Commits) Rows.Add(Follow(new RewriteRow(step)));
        SelectedRow = Rows.FirstOrDefault();
        Question.Request(commit);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Warning));
        PlanChanged();
    }

    private bool CanMoveUp() => SelectedRow is { } row && Rows.IndexOf(row) > 0;
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveTo(SelectedRow!, Rows.IndexOf(SelectedRow!) - 1);

    private bool CanMoveDown() => SelectedRow is { } row && Rows.IndexOf(row) is >= 0 and var index && index < Rows.Count - 1;
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveTo(SelectedRow!, Rows.IndexOf(SelectedRow!) + 1);

    // A list lets go of the row it moves; the moved commit is the chosen one.
    public void MoveTo(RewriteRow row, int index)
    {
        var from = Rows.IndexOf(row);
        Rows.Move(from, index);
        rewrites.Move(from, index);
        SelectedRow = row;
        PlanChanged();
    }

    private RewriteRow Follow(RewriteRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RewriteRow.Action)) return;
            rewrites.Set(Rows.IndexOf(row), row.Action);
            PlanChanged();
        };
        return row;
    }

    private int? Selected() => SelectedRow is { } row && Rows.IndexOf(row) is >= 0 and var index ? index : null;

    private void PlanChanged()
    {
        OnPropertyChanged(nameof(Refusal));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanEditMessage));
        Question.ConfirmCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRowChanged(RewriteRow? value) => PlanChanged();
}
