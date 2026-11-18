using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.Domain.Status;

namespace Trag.ViewModels.Shell;

// Which section the workspace shows, and the user's way of choosing it. Choosing the section that is already shown is a
// reselection, which the navigator announces so the feature can reload instead. The shown repository moves it too: a
// merge, rebase or revert that began, as the repository's merge, rebase and reverts tell, opens its section from anywhere
// once every feature has applied the snapshot; one that ends, like the conflicts a stash left, leaves its section only if
// it is the one shown; another repository starts over at the file status.
public sealed partial class Navigator : ObservableObject
{
    private Repository? repository;

    [ObservableProperty] public partial WorkspaceSection Section { get; set; }
    public event Action<WorkspaceSection>? Reselected;

    private void Show(WorkspaceSection section)
    {
        if (Section == section) Reselected?.Invoke(section);
        else Section = section;
    }

    [RelayCommand] public void ShowFileStatus() => Section = WorkspaceSection.FileStatus;
    [RelayCommand] public void ShowHistory() => Show(WorkspaceSection.History);
    [RelayCommand] private void ShowSearch() => Show(WorkspaceSection.Search);
    // The view of an operation exists only while that operation is in progress (WorkspaceSections).
    [RelayCommand] private void ShowMerge() => ShowExisting(WorkspaceSection.Merge);
    [RelayCommand] private void ShowRebase() => ShowExisting(WorkspaceSection.Rebase);
    [RelayCommand] private void ShowConflicts() => ShowExisting(WorkspaceSection.Conflicts);
    [RelayCommand] private void ShowPick() => ShowExisting(WorkspaceSection.Pick);
    public void ShowRepositories() => Show(WorkspaceSection.Repositories);

    // The open repository where its work goes on (WorkspaceSections).
    public void ShowOpenRepository() => Section = WorkspaceSections.Open(Operation);

    private RepositoryOperation Operation => repository?.Snapshot?.Operation ?? RepositoryOperation.None;

    private void ShowExisting(WorkspaceSection section)
    {
        if (WorkspaceSections.Exists(section, Operation)) Section = section;
    }

    // Another repository is shown: the section follows it from the file status on.
    public void Follow(Repository next)
    {
        if (repository is not null) repository.Settled -= FollowSnapshot;
        repository = next;
        next.Settled += FollowSnapshot;
        Section = WorkspaceSection.FileStatus;
    }

    private void FollowSnapshot(RepositorySnapshot snapshot, RepositorySnapshot? previous)
    {
        if (repository!.Merge.Began) Section = WorkspaceSection.Merge;
        else if (repository.Rebase.Began) Section = WorkspaceSection.Rebase;
        else if (repository.Pick.Began) Section = WorkspaceSection.Pick;
        else if (!WorkspaceSections.Exists(Section, snapshot.Operation)) Section = WorkspaceSection.FileStatus;
    }
}
