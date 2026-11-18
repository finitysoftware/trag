using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Diff;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Domain.History;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.History;

// History and Search as the screen shows them: the search text and the scope that make the filter, the command that reads more,
// the chosen commit, and the commit pane (Details, which also serves stash previews). The feed, its pages,
// when a page is read, what a write does to it and the chosen commit are the repository's history (HistoryModel); this
// tells it when a screen shows the feed and what it asks for.
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryModel history;
    private readonly Repository repository;
    private readonly Navigator navigator;
    public CommitDetailsViewModel Details { get; }
    public Task HistoryCompletion => history.Completion;

    public HistoryViewModel(HistoryModel history, DiffReader commitDiff, Navigator navigator)
    {
        this.history = history;
        repository = history.Repository;
        this.navigator = navigator;
        Details = new(history.Details, commitDiff);
        history.IsShown = CanShowFeed;
        history.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(HistoryModel.Page): OnPropertyChanged(nameof(Page)); break;
                case nameof(HistoryModel.Selected): OnPropertyChanged(nameof(SelectedCommit)); break;
                case nameof(HistoryModel.IsReading): OnPropertyChanged(nameof(IsHistoryBusy)); OnPropertyChanged(nameof(ShowHistoryCancel)); break;
            }
        };
        repository.Requery.Track(LoadMoreHistoryCommand, SearchHistoryCommand);
    }

    // Leaving a section drops its reads; history and search start over, and the commit pane belongs to them alone. A file's
    // history lasts only while History is shown.
    public void FollowSection()
    {
        if (navigator.Section != WorkspaceSection.History) FilePath = null;
        history.IsShown = CanShowFeed;
        OnPropertyChanged(nameof(IsSearch));
        OnPropertyChanged(nameof(ShowsGraph));
        OnPropertyChanged(nameof(Title));
        history.Cancel();
        if (navigator.Section is WorkspaceSection.FileStatus or WorkspaceSection.Merge) Details.Clear();
        else if (navigator.Section is WorkspaceSection.History or WorkspaceSection.Search) Restart();
    }

    // The user chose the shown section again: history and search start over, History with the whole history.
    public void Reselect(WorkspaceSection section)
    {
        if (section == WorkspaceSection.History) FilePath = null;
        if (section is WorkspaceSection.History or WorkspaceSection.Search) Restart();
    }

    [ObservableProperty] public partial string SearchQuery { get; set; } = "";
    // The file whose history the History section shows; none shows the whole history.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsGraph))] public partial string? FilePath { get; private set; }
    // A file's history lists only the commits that change the file, and a search only those that match, so neither draws
    // a graph.
    public bool ShowsGraph => FilePath is null && !IsSearch;

    // The History section opens on the file's history, or reads it in place when it is already shown.
    public void ShowFileHistory(string path)
    {
        FilePath = path;
        if (navigator.Section == WorkspaceSection.History) Restart();
        else navigator.Section = WorkspaceSection.History;
    }

    // From a file's menu: a status file Git tracks, or a commit's file by the name it has in that commit. A name that is
    // not UTF-8 cannot reach Git, which takes paths as text.
    private static bool HasStatusHistory(StatusRow? row) => row is { File.HasHistory: true };
    [RelayCommand(CanExecute = nameof(HasStatusHistory))]
    private void ShowStatusHistory(StatusRow? row) => ShowFileHistory(row!.File.Path.Display);

    private static bool HasCommitFileHistory(CommitFileRow? row) => row is { File.HasHistory: true };
    [RelayCommand(CanExecute = nameof(HasCommitFileHistory))]
    private void ShowCommitFileHistory(CommitFileRow? row) => ShowFileHistory(row!.File.Path.Display);

    public Func<IShellHost?> Host { get; set; } = () => null;

    // From the commit's menu, which chooses the commit first; a refresh can drop the choice while the menu is open.
    [RelayCommand]
    private void CopyCommitId()
    {
        if (history.Selected is { } commit) Host()?.CopyText(commit.Id);
    }

    [RelayCommand]
    private void ClearFileHistory()
    {
        FilePath = null;
        Restart();
    }
    public ShownHistoryPage Page => history.Page;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HistoryScopeTip))]
    public partial bool HistoryAllBranches { get; set; } = true;
    public HistoryCommit? SelectedCommit
    {
        get => history.Selected;
        set => history.Selected = value;
    }
    public bool IsHistoryBusy => history.IsReading;
    public string HistoryScopeTip => HistoryText.ScopeTip(HistoryAllBranches);
    public bool ShowHistoryCancel => IsHistoryBusy && !history.IsQuiet;

    [RelayCommand] private void ToggleHistoryScope() => HistoryAllBranches = !HistoryAllBranches;
    partial void OnHistoryAllBranchesChanged(bool value)
    {
        if (CanShowHistory)
            Restart(quiet: true);
    }
    public bool IsSearch => navigator.Section == WorkspaceSection.Search;
    public string Title => HistoryText.Title(IsSearch);
    partial void OnSearchQueryChanged(string value)
    {
        if (IsSearch)
            Restart(debounce: true);
    }

    private bool CanShowFeed => navigator.Section is WorkspaceSection.History or WorkspaceSection.Search;
    private bool CanShowHistory => repository.HasRepository && CanShowFeed;
    private bool CanBrowseHistory() => CanShowHistory && !repository.IsWriting && !IsHistoryBusy;
    // The list scrolled near its end reads the next page, once the one before it is in and the history goes on.
    private bool CanLoadMoreHistory() => CanBrowseHistory() && Page.HasMore;
    [RelayCommand(CanExecute = nameof(CanLoadMoreHistory))] private Task LoadMoreHistoryAsync() => history.LoadMoreAsync();
    [RelayCommand(CanExecute = nameof(CanBrowseHistory))] private void SearchHistory() => Restart();

    // The filter is what the screen asks for: the search text only while Search is shown, and the scope.
    private void Restart(bool debounce = false, bool quiet = false) =>
        history.Restart(new(IsSearch ? SearchQuery : "", HistoryAllBranches, IsSearch) { Path = FilePath }, debounce, quiet);

    // The search shown for the query; a search already shown reads again for the new query on its own.
    public void Search(string query)
    {
        SearchQuery = query;
        if (!IsSearch) navigator.Section = WorkspaceSection.Search;
    }

    // A commit asked for by its id (a tag's): the search shows it, whatever page of the history it is on, and it is
    // chosen once the search has it. A search already shown reads again for the new query on its own.
    public async Task FindCommitAsync(string id)
    {
        Search(id);
        await history.SelectAsync(id);
    }
}
