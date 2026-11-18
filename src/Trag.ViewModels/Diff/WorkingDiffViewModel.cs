using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Diff;
using Trag.Application.Live;
using Trag.Application.Status;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.Diff;

// The diff pane of the file status and of the merge and rebase views: the diff of the selected file on its side, which
// the repository's status reads; only those sections show one. It follows the selection the lists and the conflicts
// view share: the user's choice shows its diff, a choice an update makes while it edits the lists starts no read of its
// own, and once the update is published another row is read anew and the same file again, quietly, when the change may
// have touched it. A write pauses it, except one that takes a hunk of the shown diff: that diff stays on screen until the
// rest of the file replaces it in place.
public sealed partial class WorkingDiffViewModel : ObservableObject
{
    private readonly StatusModel status;
    private readonly StatusSelection selection;
    private readonly Navigator navigator;
    // The file selected when an update began to edit the lists.
    private StatusRow? before;
    // The file whose diff is on screen.
    private StatusRow? shown;
    public DiffViewModel Diff { get; }

    public WorkingDiffViewModel(StatusModel status, StatusSelection selection, DiffReader reader, Navigator navigator)
    {
        this.status = status;
        this.selection = selection;
        this.navigator = navigator;
        Diff = new(reader);
        Diff.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DiffViewModel.Document)) return;
            OnPropertyChanged(nameof(Offer));
            TakeHunkCommand.NotifyCanExecuteChanged();
            DiscardHunkCommand.NotifyCanExecuteChanged();
        };
        selection.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StatusSelection.SelectedStatus) && !status.IsEditing) Start(selection.SelectedStatus); };
        selection.ShownAgain += () => Start(selection.SelectedStatus);
        status.Editing += () => before = selection.SelectedStatus;
        status.Settled += FollowUpdate;
        var repository = status.Repository;
        // A write takes the shown diff away, except one of its own hunks or lines: the rest of the file replaces it in place.
        repository.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Repository.IsWriting) && repository.IsWriting && !repository.Writer.RunsPartOfAFile) Start(null);
        };
        repository.Requery.Track(TakeHunkCommand, DiscardHunkCommand);
        HunkDiscard = new(repository, question => status.DiscardHunkAsync(question.File, question.Lines, question.Chosen),
            question => question.Subtitle, Failures.ChangesNotDiscarded);
    }

    // What the shown diff offers to take apart from the rest of its file, a hunk or chosen lines; a working tree's diff
    // discards them too, after the question. Each is taken from the diff the lines were chosen in, which a quiet read may
    // have replaced since.
    public HunkOffer Offer => shown is { } row ? HunkOffer.For(row.File, row.FromIndex, Diff.Document.IsCompleteText) : HunkOffer.None;
    public Confirmation<HunkDiscardQuestion> HunkDiscard { get; }

    private bool CanTakeHunk() => Offer.Hunk.Length != 0 && status.CanTakeHunk(shown!.File, shown.FromIndex);
    [RelayCommand(CanExecute = nameof(CanTakeHunk))]
    private Task TakeHunkAsync(HunkChoice chosen) => status.TakeHunkAsync(shown!.File, shown.FromIndex, chosen.Document, chosen.Lines);

    private bool CanDiscardHunk() => Offer.DiscardHunk.Length != 0 && status.CanTakeHunk(shown!.File, fromIndex: false);
    [RelayCommand(CanExecute = nameof(CanDiscardHunk))]
    private void DiscardHunk(HunkChoice chosen) => HunkDiscard.Request(new(shown!.File, chosen.Document, chosen.Lines));

    // Another section was chosen: the shown diff follows it.
    public void FollowSection() => Start(selection.SelectedStatus);

    public void Start(StatusRow? row) => Show(row, quietly: false);

    // The update is published: another row is read anew, the same file is read again quietly, and only when the change may
    // have touched it or the diff on screen is not current: a read of it still runs, or the repository moved under the last.
    private void FollowUpdate(SnapshotChange change)
    {
        var now = selection.SelectedStatus;
        if (now is null || before is null || now.FromIndex != before.FromIndex || !now.File.Path.IsSame(before.File.Path)) Start(now);
        else if (change.Rereads(before.File, now.File) || !Diff.IsCurrent) Show(now, quietly: true);
    }

    private void Show(StatusRow? row, bool quietly)
    {
        if (row is null || status.DiffOf(row.File, row.FromIndex) is not { } read ||
            !WorkspaceSections.ShowsWorkingDiff(navigator.Section))
        {
            Diff.Clear();
            return;
        }

        shown = row;
        var source = new DiffSource(row.PathText, StatusText.Version(row.FromIndex));
        if (quietly) Diff.Refresh(source, read);
        else Diff.Start(source, read);
    }
}
