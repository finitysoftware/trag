using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Status;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.Discard;

// Discard for the selected unstaged files: always behind a red confirmation, because there is no undo.
// Tracked files return to the index, untracked files are deleted, staged content is kept; conflicts are left to the merge view.
public sealed partial class DiscardViewModel : ObservableObject
{
    private readonly StatusModel status;
    private readonly StatusSelection files;

    public DiscardViewModel(StatusModel status, StatusSelection files)
    {
        this.status = status;
        this.files = files;
        var repository = status.Repository;
        Confirmation = new(repository, selection => status.DiscardAsync(selection.Paths), selection => selection.Subtitle, Failures.ChangesNotDiscarded);
        repository.Requery.Track(DiscardSelectedCommand);
        files.Changed += DiscardSelectedCommand.NotifyCanExecuteChanged;
    }

    public Confirmation<DiscardQuestion> Confirmation { get; }
    // The question's "stash instead": the stash dialog, which opens in its place; the composition links it.
    public ICommand? OpenStash { get; set; }

    private bool CanDiscardSelected() => status.CanDiscard && files.HasSelected(staged: false);
    [RelayCommand(CanExecute = nameof(CanDiscardSelected))]
    private void DiscardSelected()
    {
        var selection = new DiscardQuestion(DiscardSet.From(files.SelectedFiles(staged: false)));
        if (!selection.IsEmpty) Confirmation.Request(selection);
    }

    // The dialog's escape hatch: keep everything by stashing instead of throwing it away. A stash keeps nothing a submodule
    // has checked out or changed, so when only submodules changed the question stays and says so.
    [RelayCommand]
    private void StashInsteadOfDiscard()
    {
        if (status.Repository.Snapshot is { HasChanges: false })
        {
            Confirmation.Message = StashOutcomeText.OnlySubmodules;
            return;
        }
        Confirmation.CancelCommand.Execute(null);
        OpenStash?.Execute(null);
    }
}
