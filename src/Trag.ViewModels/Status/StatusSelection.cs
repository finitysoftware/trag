using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Status;

namespace Trag.ViewModels.Status;

// The files the user selected in the staged and unstaged lists: one selection across both, which the diff pane shows,
// and a multi-selection per list for the actions. The file status keeps it through the lists' updates; the commit pane,
// the discard question and the conflicts view act on it. Changed is raised as soon as a selection changed, before the
// linked selection follows, so the commands that read it ask again.
public sealed partial class StatusSelection : ObservableObject
{
    public StatusSelection()
    {
        Staged.CollectionChanged += (_, _) => Changed?.Invoke();
        Unstaged.CollectionChanged += (_, _) => Changed?.Invoke();
    }

    public ObservableCollection<StatusRow> Staged { get; } = [];
    public ObservableCollection<StatusRow> Unstaged { get; } = [];
    public event Action? Changed;
    // The selected file's diff is to be shown again, the selection unchanged (a write ended under the view that chose it).
    public event Action? ShownAgain;

    [ObservableProperty]
    public partial StatusRow? SelectedStaged { get; set; }
    [ObservableProperty]
    public partial StatusRow? SelectedUnstaged { get; set; }
    // The diff can also show a conflict chosen in the merge panel, outside these two lists.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial StatusRow? SelectedStatus { get; set; }
    public bool HasSelection => SelectedStatus is not null;

    partial void OnSelectedStagedChanged(StatusRow? value) => Select(value, staged: true);
    partial void OnSelectedUnstagedChanged(StatusRow? value) => Select(value, staged: false);

    private void Select(StatusRow? value, bool staged)
    {
        Changed?.Invoke();
        if (value is null)
        {
            if (SelectedStaged is null && SelectedUnstaged is null)
                SelectedStatus = null;
            return;
        }
        if (staged) SelectedUnstaged = null;
        else SelectedStaged = null;
        SelectedStatus = value;
    }

    // The files of the multi-selection, or of the single row, on one side, which the actions apply to.
    public FileStatus[] SelectedFiles(bool staged) => SelectedRows(staged).Select(row => row.File).ToArray();
    public bool HasSelected(bool staged) => SelectedRows(staged).Count != 0;

    private IReadOnlyList<StatusRow> SelectedRows(bool staged)
    {
        var selection = staged ? Staged : Unstaged;
        if (selection.Count != 0)
            return selection.ToArray();
        var primary = staged ? SelectedStaged : SelectedUnstaged;
        return primary is null ? [] : [primary];
    }

    public void ShowAgain() => ShownAgain?.Invoke();

    public void ClearMultiSelection()
    {
        Staged.Clear();
        Unstaged.Clear();
    }

    public void Clear() => SelectedStaged = SelectedUnstaged = SelectedStatus = null;
}
