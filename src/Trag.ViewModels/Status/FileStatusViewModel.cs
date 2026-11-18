using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Status;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Status;

// The staged and unstaged lists and the selection across them (StatusSelection, which the diff pane beside them, the
// commit pane, the discard question and the conflicts view share). The lists are the repository's status (StatusModel),
// which prepares them for every snapshot; this remembers the selection before an update edits them and brings it back
// once they are the snapshot's. The selection's members are passed on for the views.
public sealed partial class FileStatusViewModel : ObservableObject
{
    private static readonly string[] Passed = [nameof(SelectedStaged), nameof(SelectedUnstaged), nameof(SelectedStatus), nameof(HasSelection)];
    private readonly StatusModel status;
    private readonly StatusSelection selection;
    // Counts the user's selection changes, so an update knows whether the selection it remembered is still the wanted one.
    private int selectionVersion;
    // The selection an update remembered before it edited the lists, the rows selected for the actions with it, and the
    // count it was remembered at.
    private (StatusRow? Row, StatusRow[] Rows, int Version) remembered;

    public FileStatusViewModel(StatusModel status, StatusSelection selection)
    {
        this.status = status;
        this.selection = selection;
        StagedFiles = new(status.Staged);
        UnstagedFiles = new(status.Unstaged);
        selection.PropertyChanged += FollowSelection;
        selection.Staged.CollectionChanged += (_, _) => FollowRows();
        selection.Unstaged.CollectionChanged += (_, _) => FollowRows();
        status.Replaced += () => Apply(new(status.Staged), new(status.Unstaged), SelectedStatus);
        status.Editing += () => remembered = (SelectedStatus, [.. selection.Staged, .. selection.Unstaged], selectionVersion);
        status.Edited += () =>
        {
            var current = remembered.Version == selectionVersion;
            Apply(StagedFiles, UnstagedFiles, current ? remembered.Row : SelectedStatus);
            if (current) Reselect(remembered.Rows);
        };
        // The update is published: the emptiness and the submodule notice follow the lists.
        status.Settled += _ =>
        {
            OnPropertyChanged(nameof(StagedEmpty));
            OnPropertyChanged(nameof(UnstagedEmpty));
            OnPropertyChanged(nameof(SubmoduleNotice));
        };
        status.Repository.Requery.Track(UpdateSubmodulesCommand);
    }

    public ObservableCollection<StatusRow> StagedSelection => selection.Staged;
    public ObservableCollection<StatusRow> UnstagedSelection => selection.Unstaged;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StagedEmpty))] public partial StatusList StagedFiles { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UnstagedEmpty))] public partial StatusList UnstagedFiles { get; set; }
    public StatusRow? SelectedStaged { get => selection.SelectedStaged; set => selection.SelectedStaged = value; }
    public StatusRow? SelectedUnstaged { get => selection.SelectedUnstaged; set => selection.SelectedUnstaged = value; }
    public StatusRow? SelectedStatus { get => selection.SelectedStatus; set => selection.SelectedStatus = value; }
    public bool StagedEmpty => StagedFiles.Count == 0;
    public bool UnstagedEmpty => UnstagedFiles.Count == 0;
    public bool HasSelection => selection.HasSelection;
    // Submodules not at the commits the branch records, with the update that checks those commits out.
    public string SubmoduleNotice => status.SubmodulesToUpdate is { Count: > 0 } paths ? SubmoduleText.NotAtRecorded(paths) : "";
    private bool CanUpdateSubmodules() => status.CanUpdateSubmodules;
    [RelayCommand(CanExecute = nameof(CanUpdateSubmodules))]
    private Task UpdateSubmodulesAsync() => status.UpdateSubmodulesAsync();

    // A new selected file is the user's choice unless an update is editing the lists. The views read the selection through
    // here.
    private void FollowSelection(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusSelection.SelectedStatus) && !status.IsEditing) selectionVersion++;
        if (Passed.Contains(e.PropertyName)) OnPropertyChanged(e.PropertyName);
    }

    // A row added to or taken from the selection for the actions is the user's choice too, unless an update dropped it or
    // selects it again.
    private void FollowRows()
    {
        if (!status.IsEditing) selectionVersion++;
    }

    // New lists replace the shown ones; a previous selection is kept when its row is still there.
    private void Apply(StatusList staged, StatusList unstaged, StatusRow? kept)
    {
        StagedFiles = staged;
        UnstagedFiles = unstaged;
        var list = kept?.FromIndex == true ? staged : unstaged;
        var index = list.IndexOf(kept);
        if (index < 0)
        {
            selection.Clear();
            return;
        }
        var row = (StatusRow)list[index];
        if (row.FromIndex) SelectedStaged = row;
        else SelectedUnstaged = row;
    }

    // A list drops a row it replaces from its selection, and selecting the kept row anew clears the rest; the rows that
    // were selected and are still listed are selected again, as the lists have them now.
    private void Reselect(StatusRow[] rows)
    {
        foreach (var kept in rows)
        {
            var (list, selected) = kept.FromIndex ? (StagedFiles, selection.Staged) : (UnstagedFiles, selection.Unstaged);
            var index = list.IndexOf(kept);
            if (index >= 0 && !selected.Contains(list[index])) selected.Add((StatusRow)list[index]);
        }
    }
}
