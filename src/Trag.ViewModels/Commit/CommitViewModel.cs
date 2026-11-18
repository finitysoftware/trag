using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Status;
using Trag.Application.Sync;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.ViewModels.Status;
using Trag.Domain.Status;

namespace Trag.ViewModels.Commit;

// The commit pane of the file status: the message, the stage, unstage and commit commands over the files the screen
// selected, and the last write's result. What each write needs and the writes are the repository's status; staging
// during a merge stages a resolution instead. Amending puts the draft aside and starts from the last commit's message;
// the draft comes back when the amend is turned off, done, or no longer about the last commit because HEAD moved.
// Co-authors are lines of the message itself: the picker writes one in, a row of the list takes it out.
public sealed partial class CommitViewModel : ObservableObject
{
    private readonly StatusModel status;
    private readonly Repository repository;
    private readonly StatusSelection files;
    private readonly AmendModel amend;

    public CommitViewModel(StatusModel status, AmendModel amend, CoAuthorsModel coAuthors, StatusSelection files)
    {
        this.status = status;
        this.amend = amend;
        repository = status.Repository;
        this.files = files;
        CoAuthorPicker = new(coAuthors);
        CoAuthorPicker.Chosen += person => CommitMessage = CoAuthors.With(CommitMessage, person);
        amend.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AmendModel.IsAmending)) FollowAmend();
            else if (e.PropertyName == nameof(AmendModel.Author)) FollowIdentity();
        };
        var writer = repository.Writer;
        repository.Requery.Track(StageSelectedCommand, UnstageSelectedCommand, CommitCommand);
        files.Changed += FollowSelection;
        repository.SnapshotApplied += (_, _) => { FollowIdentity(); FollowHead(); };
        repository.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Repository.HasMerge) or nameof(Repository.HasRebase)) OnPropertyChanged(nameof(UnstageTip));
            if (e.PropertyName == nameof(Repository.CanWrite)) OnPropertyChanged(nameof(CanAmend));
        };
        repository.Remotes.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RemotesModel.Overview)) OnPropertyChanged(nameof(AmendWarning)); };
        writer.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RepositoryWriter.LastWrite) && writer.LastWrite is { } outcome) Follow(outcome); };
    }

    // The writer has told the status bar how the write ended; a success drops what it was for: the committed message while
    // it is still the one that was sent, or the selection a stage or unstage acted on. A hunk leaves the file selected:
    // the rest of it is still being worked on.
    private void Follow(WriteOutcome outcome)
    {
        if (outcome.CommittedMessage is { } sent) { if (CommitMessage == sent) CommitMessage = ""; }
        else if (outcome.ClearsSelection) { files.ClearMultiSelection(); files.Clear(); }
    }

    public Repository Repository => repository;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyPropertyChangedFor(nameof(CoAuthorList), nameof(MessageHint))]
    public partial string CommitMessage { get; set; } = "";

    public CoAuthorsViewModel CoAuthorPicker { get; }
    public IReadOnlyList<GitIdentity> CoAuthorList => CoAuthors.Of(CommitMessage);
    // Why Commit waits for the message written: co-authors and nothing above them, or more than Git is offered.
    public string MessageHint => CommitRequest.MessageHint(CommitMessage);
    [RelayCommand] private void RemoveCoAuthor(GitIdentity person) => CommitMessage = CoAuthors.Without(CommitMessage, person);

    // Git's own identities, as the snapshot carries them; an amend keeps the author of the commit it replaces.
    public string AuthorText => CommitText.Author(Identities);
    public string CommitterText => CommitText.Committer(Identities);
    private RepositorySnapshot? Identities => repository.Snapshot is { } snapshot && amend.Author is { } kept ? snapshot with { Author = kept } : repository.Snapshot;
    public string SigningText => CommitText.Signing(repository.Snapshot?.Signing ?? CommitSigning.None);

    // The checkbox over the repository's amend: turned on, it starts from the last commit's message; turned off, or ended
    // by the amend, the message put aside comes back.
    public bool IsAmending
    {
        get => amend.IsAmending;
        set
        {
            if (value == amend.IsAmending) return;
            if (value) AmendCompletion = ShowLastMessageAsync();
            else amend.End();
        }
    }
    // A last commit to replace, and nothing else being written.
    public bool CanAmend => amend.CanOffer;
    // The read of the last commit's message, which the box shows once it arrives.
    public Task AmendCompletion { get; private set; } = Task.CompletedTask;
    public string AmendWarning => IsAmending ? amend.Warning : "";
    public string CommitActionText => CommitText.Action(IsAmending);
    public string CommitTip => CommitText.Tip(IsAmending);

    private void FollowAmend()
    {
        OnPropertyChanged(nameof(IsAmending));
        OnPropertyChanged(nameof(AmendWarning));
        OnPropertyChanged(nameof(CommitActionText));
        OnPropertyChanged(nameof(CommitTip));
        CommitCommand.NotifyCanExecuteChanged();
        if (!amend.IsAmending) CommitMessage = amend.Draft;
    }

    private async Task ShowLastMessageAsync()
    {
        if (await amend.BeginAsync(CommitMessage) is { } message) CommitMessage = message;
    }

    private void FollowHead() => OnPropertyChanged(nameof(CanAmend));

    [ObservableProperty]
    public partial string StageActionText { get; set; } = CommitText.StageAction(0);

    [ObservableProperty]
    public partial string UnstageActionText { get; set; } = CommitText.UnstageAction(0);

    // Unstage waits while a merge or rebase is in progress; the button says where a resolution is changed instead.
    public string UnstageTip => ConflictScope.UnstageTipFor(repository.Snapshot);
    private bool CanStage() => status.CanStage && files.HasSelected(staged: false);
    private bool CanUnstage() => status.CanUnstage && files.HasSelected(staged: true);
    private bool CanCommit() => IsAmending ? amend.CanAmend(CommitMessage) : status.CanCommit(CommitMessage);
    [RelayCommand(CanExecute = nameof(CanStage))]
    private Task StageSelectedAsync() => status.StageAsync(files.SelectedFiles(false));
    [RelayCommand(CanExecute = nameof(CanUnstage))]
    private Task UnstageSelectedAsync() => status.UnstageAsync(files.SelectedFiles(true));
    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task CommitAsync() => IsAmending ? amend.AmendAsync(CommitMessage) : status.CommitAsync(CommitMessage);
    // The selection changed: the three commands and the button captions follow. The write preconditions reach the
    // commands through the repository's requery.
    private void FollowSelection()
    {
        StageSelectedCommand.NotifyCanExecuteChanged();
        UnstageSelectedCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
        StageActionText = CommitText.StageAction(files.Unstaged.Count);
        UnstageActionText = CommitText.UnstageAction(files.Staged.Count);
    }
    private void FollowIdentity()
    {
        OnPropertyChanged(nameof(AuthorText));
        OnPropertyChanged(nameof(CommitterText));
        OnPropertyChanged(nameof(SigningText));
    }
}
