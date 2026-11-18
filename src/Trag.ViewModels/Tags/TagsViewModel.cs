using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Tags;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Tags;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Tags;

// Tags as the screen shows them: the create dialog over the commit chosen in History, and the sidebar section with the
// tags as rows under a filter, where a row finds its commit in the history, is pushed to the remote, or goes after a
// question. The list and every write are the repository's tags (TagsModel).
public sealed partial class TagsViewModel : ObservableObject
{
    private readonly TagsModel tags;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public TagsViewModel(TagsModel tags, HistoryModel history)
    {
        this.tags = tags;
        repository = tags.Repository;
        this.history = history;
        Delete = new(repository, row => tags.DeleteAsync(row.Tag.Name, DeleteOnRemote),
            row => TagText.DeleteQuestion(row.Tag.Name, row.Tag.Commit), Failures.TagNotDeleted);
        Delete.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Confirmation<TagRow>.Subject)) OnPropertyChanged(nameof(DeleteTitle)); };
        List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(List.Value)) FollowList(); };
        Draft.PropertyChanged += (_, _) => CreateTagCommand.NotifyCanExecuteChanged();
        repository.SnapshotApplied += (_, _) => FollowRemote();
        repository.Requery.Track(OpenCreateTagCommand, CreateTagCommand, PushCommand, RequestDeleteCommand);
    }

    public TagDraft Draft { get; } = new();
    // The repository's list, and its tags as the section shows them.
    public Loader<IReadOnlyList<TagInfo>> List => tags.List;
    // The tags under the filter, as rows.
    [ObservableProperty] public partial IReadOnlyList<TagRow> Visible { get; private set; } = [];
    [ObservableProperty] public partial string TagSearch { get; set; } = "";
    // The sidebar flyout; a row action closes it because the result shows elsewhere.
    [ObservableProperty] public partial bool IsPickerOpen { get; set; }
    public Confirmation<TagRow> Delete { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsDeleteOnRemoteHint))] public partial bool DeleteOnRemote { get; set; }
    // Finds a commit in the history by its id; the composition links it to the history screen.
    public Func<string, Task> FindCommit { get; set; } = _ => Task.CompletedTask;

    // The section exists only with a tag; the list itself arrives a moment later.
    public bool HasTags => tags.HasTags;
    public string TagButtonText => TagList.ButtonText(List.Value);
    public string TagCountText => TagList.CountText(List.Value);
    // A remote to push a tag to, or to delete it on too.
    public bool CanPushTags => tags.PushRemote is not null;
    public string PushTip => tags.PushRemote is { } remote ? TagText.PushTo(remote) : "";
    public string DeleteOnRemoteLabel => tags.PushRemote is { } remote ? TagText.DeleteOn(remote) : "";
    // A fetch brings back what the remote still has, which the question says while the remote is left alone.
    public string DeleteOnRemoteHint => tags.PushRemote is { } remote ? TagText.DeleteOnHint(remote) : "";
    public bool ShowsDeleteOnRemoteHint => CanPushTags && !DeleteOnRemote;
    public string DeleteTitle => Delete.Subject is { } row ? TagText.DeleteTitle(row.Tag.Name) : "";

    partial void OnTagSearchChanged(string value) => Visible = Filter(value);

    private TagRow[] Filter(string text) => [.. TagList.Filter(List.Value, text).Select(tag => new TagRow(tag))];

    // A new list becomes new rows under the filter; a question about a tag that is gone closes.
    private void FollowList()
    {
        Visible = Filter(TagSearch);
        OnPropertyChanged(nameof(TagButtonText));
        OnPropertyChanged(nameof(TagCountText));
        if (Delete.Subject is { } subject && List.Value.All(tag => tag.Name != subject.Tag.Name)) Delete.Close();
    }

    private void FollowRemote()
    {
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(CanPushTags));
        OnPropertyChanged(nameof(PushTip));
        OnPropertyChanged(nameof(DeleteOnRemoteLabel));
        OnPropertyChanged(nameof(DeleteOnRemoteHint));
        OnPropertyChanged(nameof(ShowsDeleteOnRemoteHint));
    }

    private bool CanOpenCreateTag() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanOpenCreateTag))]
    private void OpenCreateTag()
    {
        if (history.Selected is { } commit) Draft.Open(commit, tags.PushRemote, tags.SignsTags);
    }

    [RelayCommand] private void CancelCreateTag() => Draft.Close();

    private bool CanCreateTag() => Draft.IsOpen && repository.CanWrite && Draft.IsValid;
    [RelayCommand(CanExecute = nameof(CanCreateTag))]
    private async Task CreateTagAsync()
    {
        if (!Draft.IsOpen) return;
        await Draft.SubmitAsync(() => tags.CreateAsync(Draft.ToOperation(), Draft.PushTo), Failures.TagNotCreated);
    }

    // The list follows the repository; opening it waits only for a read still in flight.
    [RelayCommand]
    private Task OpenTagPickerAsync()
    {
        TagSearch = "";
        return List.Completion;
    }

    [RelayCommand]
    private Task ShowAsync(TagRow row)
    {
        IsPickerOpen = false;
        return FindCommit(row.Tag.Commit);
    }

    private bool CanAct(TagRow? row) => row is not null && repository.CanWrite;
    private bool CanPush(TagRow? row) => CanAct(row) && CanPushTags;

    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync(TagRow? row)
    {
        if (row is null) return;
        IsPickerOpen = false;
        await tags.PushAsync(row.Tag.Name);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void RequestDelete(TagRow? row)
    {
        if (row is null) return;
        IsPickerOpen = false;
        DeleteOnRemote = false;
        Delete.Request(row);
    }
}
