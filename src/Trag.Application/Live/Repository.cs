using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Branches;
using Trag.Application.CherryPick;
using Trag.Application.Details;
using Trag.Application.Files;
using Trag.Application.Reset;
using Trag.Application.History;
using Trag.Application.Merge;
using Trag.Application.Rebase;
using Trag.Application.Revert;
using Trag.Application.Stash;
using Trag.Application.Status;
using Trag.Application.Sync;
using Trag.Application.Tags;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.Live;

// The repository as the shell shows it: the verified snapshot, whether a read or a write is in progress, whether the
// last write still needs a review, and the writer every write of it goes through. Every write precondition is derived
// here, so a feature asks CanWrite or CanMerge and adds only what is its own. Its parts, from the status to the history,
// are models of their own, built here over the session's services, so they follow each snapshot before any screen does. Features keep their own notion of what changed by comparing the applied snapshot
// with the previous one in SnapshotApplied. A repository is never emptied: another one takes its place. Every change
// ends in one requery pass.
public sealed partial class Repository : ObservableObject
{
    // A repository over nothing reads and writes nothing; a test states a writer it does not use.
    public Repository(bool hasWriter = false) : this(RepositoryServices.None(), hasWriter) { }

    public Repository(RepositoryServices services) : this(services, services.Workspace is not null) { }

    private Repository(RepositoryServices services, bool hasWriter)
    {
        HasWriter = hasWriter;
        Time = services.Time;
        Writer = new(services.Workspace, services.StatusBar, this, services.Dispatcher, services.Defects);
        // The shell's Cancel reaches the write in flight.
        Cancels.Register(() => false, Writer.Cancel);
        Status = new(this, services.Dispatcher, services.LiveWorkspace);
        Amend = new(this, services.HistoryWorkspace, services.StatusBar);
        CoAuthors = new(this, services.HistoryWorkspace);
        Stashes = new(this, services.BranchWorkspace);
        Branches = new(this, services.BranchWorkspace, services.StatusBar);
        Tags = new(this, services.BranchWorkspace);
        Remotes = new(this, services.SyncWorkspace, services.StatusBar);
        Conflicts = new(this, services.LiveWorkspace);
        // The merge and the rebase count the conflicts' files, so they follow each snapshot after them; a merge pushes
        // through the remotes.
        Merge = new(this, services.BranchWorkspace, new MergeFlow(Writer, Remotes, services.StatusBar));
        Rebase = new(this, services.BranchWorkspace);
        Revert = new(this);
        Pick = new(this);
        CherryPick = new(this, services.BranchWorkspace);
        Reset = new(this, services.BranchWorkspace);
        Rewrite = new(this, services.BranchWorkspace);
        RemoteSettings = new(this, services.BranchWorkspace);
        Details = new(this, services.HistoryWorkspace);
        History = new(this, services.HistoryWorkspace);
        Blame = new(this, services.HistoryWorkspace);
        Files = new(this, services.StatusBar);
        Versions = new(this, services.HistoryWorkspace, services.StatusBar);
    }

    public RepositorySnapshot? Snapshot { get; private set; }
    // The header's texts over the snapshot: the repository's name, the branch, Git's version and what the state holds back.
    [ObservableProperty] public partial RepositoryHeader Header { get; private set; } = RepositoryHeader.Empty;
    // A repository is shown exactly while a snapshot is; nothing sets this on its own.
    public bool HasRepository => Snapshot is not null;
    public bool HasMerge => Snapshot?.Operation == RepositoryOperation.Merge;
    public bool HasRebase => Snapshot?.Operation == RepositoryOperation.Rebase;
    // Conflicts with no merge or rebase in progress, as a stash applied over changed files leaves them.
    public bool HasStashConflicts => Snapshot?.Operation == RepositoryOperation.Conflict;
    // A revert or a cherry-pick in progress.
    public bool HasPick => Snapshot?.Operation is RepositoryOperation.Revert or RepositoryOperation.CherryPick;
    // A read-only or preview shell has no writer at all.
    public bool HasWriter { get; }
    // The session's clock, which the remotes, the stashes and the blame tell their times by.
    public TimeProvider Time { get; }
    public RepositoryWriter Writer { get; }
    public StatusModel Status { get; }
    public AmendModel Amend { get; }
    public CoAuthorsModel CoAuthors { get; }
    public StashesModel Stashes { get; }
    public BranchesModel Branches { get; }
    public TagsModel Tags { get; }
    public RemotesModel Remotes { get; }
    public ConflictsModel Conflicts { get; }
    public MergeModel Merge { get; }
    public RebaseModel Rebase { get; }
    public RevertModel Revert { get; }
    public PickModel Pick { get; }
    public CherryPickModel CherryPick { get; }
    public ResetModel Reset { get; }
    public RewriteModel Rewrite { get; }
    public RemoteSettingsModel RemoteSettings { get; }
    public RepositoryDetailsModel Details { get; }
    public HistoryModel History { get; }
    public BlameModel Blame { get; }
    public WorkingFilesModel Files { get; }
    public FileVersionsModel Versions { get; }
    // The session holds IsReading around an open or refresh, the writer IsWriting around a write.
    [ObservableProperty] public partial bool IsReading { get; set; }
    [ObservableProperty] public partial bool IsWriting { get; set; }
    // Set when an outcome could not be verified; every write waits for a refresh until it is cleared.
    [ObservableProperty] public partial bool NeedsReview { get; set; }
    public bool IsBusy => IsReading || IsWriting;
    // The common precondition of every write: a writer, a repository, nothing in flight, no unreviewed outcome.
    public bool IsWritable => HasWriter && HasRepository && !IsBusy && !NeedsReview;
    public bool CanWrite => IsWritable && Snapshot is { HasUnsupportedWriteState: false };
    // Merge writes need a merge Trag understands, rebase writes a rebase it understands; every other write refuses both.
    public bool CanMerge => IsWritable && Snapshot is { HasSupportedMerge: true };
    public bool CanRebase => IsWritable && Snapshot is { HasSupportedRebase: true };
    // Starting a merge or a rebase needs a branch with commits and no state Trag does not handle.
    public bool CanStartOperation => IsWritable && Snapshot is { CanStartOperation: true };
    // Finishing or aborting a revert needs a revert Trag understands.
    public bool CanFinishPick => IsWritable && Snapshot is { HasSupportedPick: true };
    // Resolving a conflict is the same during either, and for conflicts a stash left with neither in progress.
    public bool CanResolve => IsWritable && Snapshot is { HasSupportedResolution: true };
    // Every command whose CanExecute reads this state is tracked here.
    public Requery Requery { get; } = new();
    // The reads and the write of this repository that the shell's Cancel reaches.
    public CancelRegistry Cancels { get; } = new();
    // Set when the application goes: a write's result that arrives later reaches nothing. Opening or closing another
    // repository never retires one, as neither starts while a write runs.
    public bool IsRetired { get; private set; }
    public void Retire() => IsRetired = true;

    public event Action<RepositorySnapshot, RepositorySnapshot?>? SnapshotApplied;
    // Raised last, with the snapshot now shown and the one before, after the followers of SnapshotApplied and before the
    // requery: whatever needs every feature to have followed the change comes here, as the navigator does, so the merge
    // view opens on conflicts that are already listed.
    public event Action<RepositorySnapshot, RepositorySnapshot?>? Settled;

    public void Apply(RepositorySnapshot snapshot)
    {
        var previous = Snapshot;
        var (hadMerge, hadRebase, hadConflicts, hadPick) = (HasMerge, HasRebase, HasStashConflicts, HasPick);
        Snapshot = snapshot;
        Header = RepositoryHeader.From(snapshot);
        if (previous is null) OnPropertyChanged(nameof(HasRepository));
        if (hadMerge != HasMerge) OnPropertyChanged(nameof(HasMerge));
        if (hadRebase != HasRebase) OnPropertyChanged(nameof(HasRebase));
        if (hadConflicts != HasStashConflicts) OnPropertyChanged(nameof(HasStashConflicts));
        if (hadPick != HasPick) OnPropertyChanged(nameof(HasPick));
        SnapshotApplied?.Invoke(snapshot, previous);
        Settled?.Invoke(snapshot, previous);
        NotifyWritability();
    }

    // An explicit refresh read the repository: it answered whatever the last write left open, and the remotes, whose state
    // is not part of the snapshot, read theirs again. The watcher does not complete a refresh: it fires on every file change.
    public void CompleteRefresh()
    {
        NeedsReview = false;
        Remotes.FollowRefresh();
    }

    partial void OnIsReadingChanged(bool value) => NotifyActivity();
    partial void OnIsWritingChanged(bool value) => NotifyActivity();
    partial void OnNeedsReviewChanged(bool value) => NotifyWritability();

    private void NotifyActivity()
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyWritability();
    }

    // One requery pass after the change is complete: the commands ask the derived preconditions again. Only CanWrite is
    // followed by itself, by the commit's amend.
    private void NotifyWritability()
    {
        OnPropertyChanged(nameof(CanWrite));
        Requery.Invalidate();
    }
}
