using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.ViewModels.Branches;
using Trag.ViewModels.CherryPick;
using Trag.ViewModels.Files;
using Trag.ViewModels.Reset;
using Trag.ViewModels.Commit;
using Trag.ViewModels.Details;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Discard;
using Trag.ViewModels.History;
using Trag.ViewModels.Merge;
using Trag.ViewModels.Publish;
using Trag.ViewModels.Rebase;
using Trag.ViewModels.Setup;
using Trag.ViewModels.Repositories;
using Trag.ViewModels.Revert;
using Trag.ViewModels.Stash;
using Trag.ViewModels.Status;
using Trag.ViewModels.Sync;
using Trag.ViewModels.Tags;

namespace Trag.ViewModels.Shell;

// Window content and top-level commands over the application's parts, which stay for the window's lifetime, and the
// workspace: the features of the open repository.
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ApplicationFeatures application;

    public Workbench Workbench => application.Workbench;
    public RepositorySession Session => Workbench.Session;
    // The features of the repository shown now; Show puts another repository's in their place.
    private WorkspaceFeatures Workspace { get; set; }
    public Repository Repository => Workspace.Repository;
    public StatusBarState StatusBar => Workbench.StatusBar;
    public Navigator Navigator => application.Navigator;
    public LayoutPreferences Layout => Workbench.Layout;
    public RepositoryCatalogViewModel Catalog => application.Catalog;
    public AutoFetchViewModel AutoFetch => application.AutoFetch;
    public GitSetupViewModel Git => application.Git;
    public FileStatusViewModel FileStatus => Workspace.FileStatus;
    public WorkingDiffViewModel WorkingDiff => Workspace.WorkingDiff;
    public CommitViewModel Commit => Workspace.Commit;
    public DiscardViewModel Discard => Workspace.Discard;
    public StashViewModel Stash => Workspace.Stash;
    public BranchesViewModel Branches => Workspace.Branches;
    public TagsViewModel Tags => Workspace.Tags;
    public RevertViewModel Revert => Workspace.Revert;
    public PickViewModel Pick => Workspace.Pick;
    public CherryPickViewModel CherryPick => Workspace.CherryPick;
    public ResetViewModel Reset => Workspace.Reset;
    public RewriteViewModel Rewrite => Workspace.Rewrite;
    public RemoteSettingsViewModel RemoteSettings => Workspace.RemoteSettings;
    public RepositoryDetailsViewModel RepositoryDetails => Workspace.RepositoryDetails;
    public GoToCommitViewModel GoTo => Workspace.GoTo;
    public ResolutionViewModel Resolution => Workspace.Resolution;
    public MergeViewModel Merge => Workspace.Merge;
    public RebaseViewModel Rebase => Workspace.Rebase;
    public MergeDialogViewModel MergeDialog => Workspace.MergeDialog;
    public HistoryViewModel History => Workspace.History;
    public BlameViewModel Blame => Workspace.Blame;
    public FileActionsViewModel Files => Workspace.Files;
    public SyncViewModel Sync => Workspace.Sync;
    public PublishViewModel Publish => Workspace.Publish;
    // The pages the section views are typed on.
    public FileStatusPage FileStatusPage { get; private set; }
    public HistoryPage HistoryPage { get; private set; }
    public MergePage MergePage { get; private set; }
    public RebasePage RebasePage { get; private set; }
    // The conflicts a stash left, shown with the panels the merge and the rebase views share.
    public ConflictsPage ConflictsPage { get; private set; }
    public PickPage PickPage { get; private set; }
    // The window's services while a window shows this shell; every feature reaches them through here.
    public IShellHost? Host { get; set; }
    public RepositoryHeader Header => Repository.Header;
    // Whether the status bar shows the last operation's git output; it folds away once none is left.
    [ObservableProperty] public partial bool IsSyncDetailsExpanded { get; set; }

    public ShellViewModel(ApplicationFeatures application, WorkspaceFeatures workspace)
    {
        this.application = application;
        Workspace = workspace;
        ComposePages();
        Follow(Repository);
        FollowViewChanges();
    }

    // Another repository is shown: its features take the place of the previous one's, whose links to the application the
    // composition has undone, and every property is announced again (an empty name), since most read the workspace.
    public void Show(WorkspaceFeatures workspace)
    {
        Unfollow(Repository);
        Workspace = workspace;
        ComposePages();
        Follow(Repository);
        OnPropertyChanged(string.Empty);
    }

    // The pages the section views are typed on.
    [MemberNotNull(nameof(FileStatusPage), nameof(HistoryPage), nameof(MergePage), nameof(RebasePage), nameof(ConflictsPage), nameof(PickPage))]
    private void ComposePages()
    {
        FileStatusPage = new(FileStatus, Commit, Discard, WorkingDiff, Layout, Repository, OpenCommand, Resolution, Workspace.ForcePush, History, Blame, Files);
        HistoryPage = new(History, GoTo, Stash, Branches, Tags, Revert, CherryPick, Reset, Rewrite, Blame, Files, Layout, CancelCommand);
        ConflictsPage = new(Resolution, Workspace.Conflict, WorkingDiff, Layout, Navigator);
        MergePage = new(Merge, ConflictsPage, Commit, Repository, Navigator);
        RebasePage = new(Rebase, ConflictsPage, Repository, Navigator);
        PickPage = new(Pick, ConflictsPage, Navigator);
    }

    // The header and the section column follow the shown repository.
    private void Follow(Repository repository) => repository.PropertyChanged += FollowRepository;

    private void Unfollow(Repository repository) => repository.PropertyChanged -= FollowRepository;

    private void FollowRepository(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Repository.Header)) OnPropertyChanged(nameof(Header));
        else if (e.PropertyName == nameof(Repository.HasRepository)) OnPropertyChanged(nameof(ShowsRepositoryActions));
        else if (e.PropertyName is nameof(Repository.HasMerge) or nameof(Repository.HasRebase)) OnPropertyChanged(nameof(CurrentSection));
    }

    private void FollowViewChanges()
    {
        Workbench.Cancels.Changed += CancelCommand.NotifyCanExecuteChanged;
        Workbench.Cancels.Changed += () => OnPropertyChanged(nameof(ShowsCancel));
        Session.Requery.Track(PrepareCommitCommand, OpenCommand, RefreshCommand, CloseRepositoryCommand, OpenSuperprojectCommand);
        StatusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarState.HasOperationDetails) && !StatusBar.HasOperationDetails) IsSyncDetailsExpanded = false;
        };
        Navigator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Navigator.Section)) return;
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(ShowsWorkspace));
            OnPropertyChanged(nameof(ShowsRepositoryActions));
            OnPropertyChanged(nameof(CurrentSection));
        };
        Catalog.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogState.IsChoosingFolder)) OpenCommand.NotifyCanExecuteChanged();
        };
    }

    // The synthetic preview (a diagnostics fixture) while it is shown; it takes the section's place.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CurrentSection))] public partial object? Preview { get; set; }

    // The repository list while the picker is shown, else null: the workspace stays alive underneath.
    public object? CurrentPage => Navigator.Section == WorkspaceSection.Repositories ? Catalog : null;
    // The workspace shows unless the repository list does; the repository's own actions show with it over a repository.
    public bool ShowsWorkspace => CurrentPage is null;
    public bool ShowsRepositoryActions => ShowsWorkspace && Repository.HasRepository;
    // What the workspace's section column shows: history or search, the merge or the rebase, the preview. Null is the
    // file status view, which is never dropped, so its lists keep their rows.
    public object? CurrentSection
    {
        get
        {
            if (Preview is not null) return Preview;
            if (!WorkspaceSections.Exists(Navigator.Section, Repository.Snapshot?.Operation ?? RepositoryOperation.None)) return null;
            return Navigator.Section switch
            {
                WorkspaceSection.History or WorkspaceSection.Search => HistoryPage,
                WorkspaceSection.Merge => MergePage,
                WorkspaceSection.Rebase => RebasePage,
                WorkspaceSection.Conflicts => ConflictsPage,
                WorkspaceSection.Pick => PickPage,
                _ => null
            };
        }
    }

    // Called by the App dispatcher only after the window has opened: the workbench starts, and without a repository to
    // show, the picker is the first screen.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!await Workbench.StartAsync(ct, Git.StartAsync)) return;
        if (!Repository.HasRepository && !Repository.IsBusy) Navigator.ShowRepositories();
    }
    // Open shows the repository list, where folders are added and opened.
    private bool CanOpen() => !Catalog.State.IsChoosingFolder && !Repository.IsWriting;
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => Navigator.ShowRepositories();
    // Refresh reads the open repository again and Close shows none: the session's work, the window's commands.
    private bool CanRefresh() => Session.CanRefresh;
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => Session.RefreshAsync();
    private bool CanCloseRepository() => Session.CanClose;
    [RelayCommand(CanExecute = nameof(CanCloseRepository))]
    private Task CloseRepositoryAsync() => Session.CloseRepositoryAsync();
    // A terminal in the open repository's folder; nothing without one.
    [RelayCommand]
    private void OpenTerminal()
    {
        if (Repository.Snapshot is { } snapshot) Host?.OpenTerminal(snapshot.Root);
    }
    // A submodule leads back to the repository it belongs to, once no write holds it.
    private bool CanOpenSuperproject() => !Repository.IsWriting;
    [RelayCommand(CanExecute = nameof(CanOpenSuperproject))]
    private Task OpenSuperprojectAsync() => Repository.Snapshot?.Superproject is { } superproject ? Session.OpenRepositoryAsync(superproject) : Task.CompletedTask;

    // The status bar offers Cancel while the shown repository is read or written, the repository list is read or a clone
    // runs; the shown repository's other reads offer it where they show.
    public bool ShowsCancel => Repository.IsBusy || Workbench.Cancels.CanCancel;
    private bool CanCancel() => Workbench.CanCancel;
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Workbench.Cancel();

    // The features let go of the application, the workbench stops what runs, then it releases the workspace and waits for
    // the layout save.
    public async ValueTask DisposeAsync()
    {
        Workspace.Detach();
        Workbench.Stop();
        await Workbench.DisposeAsync();
    }
    private bool CanPrepareCommit() => Repository.HasRepository && !Repository.IsWriting;
    [RelayCommand(CanExecute = nameof(CanPrepareCommit))]
    private void PrepareCommit() => Navigator.ShowOpenRepository();
}
