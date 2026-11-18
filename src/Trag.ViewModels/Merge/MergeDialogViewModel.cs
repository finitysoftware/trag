using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Rebase;
using Trag.Domain.Presentation;

namespace Trag.ViewModels.Merge;

// The merge dialog with its two ways of bringing a branch in: merging (MergeBranchesViewModel) and rebasing
// (RebaseBranchViewModel). The toolbar and the picker open it on Merge; the switch at its top hands the chosen branch to
// the other side. The dialog is open while either side is, and opens again only once both are closed.
public sealed partial class MergeDialogViewModel : ObservableObject
{
    public MergeDialogViewModel(MergeBranchesViewModel merge, RebaseBranchViewModel rebase, Repository repository)
    {
        Merge = merge;
        Rebase = rebase;
        merge.Draft.PropertyChanged += (_, e) => Follow(e.PropertyName, merge.Draft, rebase: false);
        rebase.Draft.PropertyChanged += (_, e) => Follow(e.PropertyName, rebase.Draft, rebase: true);
        merge.PropertyChanged += (_, _) => repository.Requery.Invalidate();
        rebase.PropertyChanged += (_, _) => repository.Requery.Invalidate();
        repository.Requery.Track(OpenCommand, ShowMergeCommand, ShowRebaseCommand);
    }

    public MergeBranchesViewModel Merge { get; }
    public RebaseBranchViewModel Rebase { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Icon))]
    [NotifyCanExecuteChangedFor(nameof(ShowMergeCommand), nameof(ShowRebaseCommand))]
    public partial bool IsRebase { get; private set; }

    public bool IsOpen => Merge.Draft.IsOpen || Rebase.Draft.IsOpen;
    public string Title => IsRebase ? RebaseText.DialogTitle : MergeText.DialogTitle;
    public string Subtitle => IsRebase ? RebaseText.DialogSubtitle : MergeText.DialogSubtitle;
    public string Icon => IsRebase ? IconKinds.Rebase : IconKinds.Merge;

    // The side that opens is the side shown.
    private void Follow(string? property, Form draft, bool rebase)
    {
        if (property != nameof(Form.IsOpen)) return;
        if (draft.IsOpen) IsRebase = rebase;
        OnPropertyChanged(nameof(IsOpen));
        OpenCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpen() => !IsOpen && Merge.OpenCommand.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync() => CanOpen() ? Merge.OpenForAsync(null) : Task.CompletedTask;

    private bool CanSwitch() => IsOpen && !Merge.IsMerging && !Rebase.IsRebasing;

    // The branch the merge choice hands over goes across (MergeChoice.RebaseOnto); the new side opens before the old one
    // closes, so the dialog stays up.
    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private async Task ShowRebaseAsync()
    {
        if (!CanSwitch() || IsRebase) return;
        var opening = Rebase.OpenForAsync(Merge.Draft.Choice.RebaseOnto);
        Merge.CancelCommand.Execute(null);
        await opening;
    }

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private async Task ShowMergeAsync()
    {
        if (!CanSwitch() || !IsRebase) return;
        var opening = Merge.OpenForAsync(Rebase.Draft.Onto?.Branch);
        Rebase.Close();
        await opening;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsRebase) Rebase.CancelCommand.Execute(null);
        else Merge.CancelCommand.Execute(null);
    }
}
