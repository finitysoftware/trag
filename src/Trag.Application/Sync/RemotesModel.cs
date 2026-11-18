using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Application.Publish;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.Application.Sync;

// The repository's remotes as sync sees them. The target the current branch syncs with: a remote, the tracking one until
// the user picks another, and an upstream branch, Git's until the user picks another. What Git says about that target
// alone (Overview): read when the target changes; read again, keeping what it shows, when a fetch moved a remote-tracking
// ref, when an explicit refresh may have changed Git configuration and when asked; a write pauses the read and its end
// reads again. When each remote was last fetched since the repository opened, the user's fetch or pull and the quiet
// fetch alike: Git keeps no record of a fetch that brought nothing, so this is the one place that knows, and the overview
// and the automatic fetch take it from here. And the writes that fetch, pull, push, force push, choose the account a
// remote uses and publish.
public sealed partial class RemotesModel : ObservableObject
{
    private readonly ISyncWorkspace? reader;
    private readonly StatusBarState statusBar;
    private readonly Loader<SyncOverview> read;
    private readonly Dictionary<string, DateTimeOffset> fetched = new(StringComparer.Ordinal);
    // Apply may finish its read before the explicit refresh event arrives. That refresh must not read twice.
    private bool readOnLastApply;
    // Following a snapshot ends in the repository's own requery pass; what changes later needs a pass of its own.
    private bool following;

    public RemotesModel(Repository repository, ISyncWorkspace? reader, StatusBarState statusBar)
    {
        Repository = repository;
        this.reader = reader;
        this.statusBar = statusBar;
        read = new(SyncOverview.Empty(), repository.Cancels)
        {
            ChangedMessage = SyncText.Changed,
            CanceledMessage = SyncText.Canceled,
            FailedMessage = SyncText.Failed,
            DescribeFailure = error => (error as AccountException)?.Message
        };
        read.PropertyChanged += FollowRead;
        Overview.Accounts.PropertyChanged += FollowAccountChoice;
        repository.SnapshotApplied += (snapshot, _) => Apply(snapshot);
        repository.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Repository.IsWriting)) return;
            if (repository.IsWriting) read.Cancel();
            else Begin();
        };
    }

    public Repository Repository { get; }
    // The remote chosen for the current branch and the snapshot it was chosen for, and the upstream choice.
    public SyncTargetSelection Target { get; private set; } = SyncTargetSelection.Empty;
    public SyncBranchChoice Upstream { get; private set; } = SyncBranchChoice.None;
    [ObservableProperty] public partial SyncOverview Overview { get; private set; } = SyncOverview.Empty();
    public bool IsReading => read.IsBusy;
    public Task Completion => read.Completion;

    // Raised with the target and its upstream choice already set: TargetFollowed when a snapshot gave the target another
    // snapshot, or it went; ChoiceChanged when the same target's choice changed, by the user or by the branches a read offered.
    public event Action? TargetFollowed;
    public event Action? ChoiceChanged;
    // Raised when a fetch of the target succeeded, with its time: the automatic fetch counts it as one of its own.
    public event Action<DateTimeOffset>? Fetched;

    // The target is chosen while the repository is idle; the writes also wait for the read of their target.
    public bool CanChooseTarget => Repository.HasRepository && !Repository.IsBusy;
    public bool CanReread => CanChooseTarget && !IsReading;
    public bool CanSynchronize => Repository.IsWritable && !IsReading && Repository.Snapshot is { HasUnsupportedState: false } && Overview.CanSynchronize;
    public bool CanSaveAccount => Repository.HasWriter && CanChooseTarget && !IsReading && Overview.Accounts.CanSave;
    public bool CanPull => CanSynchronize && Overview.State?.CanPull(Repository.Snapshot!) == true;
    public bool CanPush => CanSynchronize && Overview.State?.CanTransfer(Repository.Snapshot!) == true;
    public bool CanForcePush => CanSynchronize && Overview.State?.CanForcePush(Repository.Snapshot!) == true;
    // What the file status banner says about the target read for the branch shown.
    public ForcePushSummary ForcePush => new(Overview.State, Repository.Snapshot);
    // The repository shown as publishing starts from it, named after its folder.
    public PublishContext PublishContext => Repository.Snapshot is { } snapshot ? new(snapshot, RepositoryEntry.DisplayName(snapshot.Root)) : PublishContext.Empty;
    // The publish the dialog confirmed, busy until it ends; it runs as the repository's write, which Cancel stops where Git
    // can stop.
    public Operation Publishing { get; } = new();
    public void CancelPublish() => Repository.Writer.Cancel();
    // The fetch nobody waits for: only a writable repository on a branch with an existing upstream, in no operation, with
    // no write running and the account confirmed.
    public bool CanFetchQuietly => reader is not null && Repository.IsWritable && !Repository.IsWriting
        && Repository.Snapshot is { HasUnsupportedState: false } snapshot
        && Overview.CanSynchronize && Overview.State is { HasUpstream: true } target && target.CanTransfer(snapshot);

    // A new panel is new state for every command that reads it, the account choice among them.
    partial void OnOverviewChanging(SyncOverview value) => Overview.Accounts.PropertyChanged -= FollowAccountChoice;
    partial void OnOverviewChanged(SyncOverview value)
    {
        value.Accounts.PropertyChanged += FollowAccountChoice;
        if (!following) Repository.Requery.Invalidate();
    }

    // Another account chosen changes what Save and the sync commands may do.
    private void FollowAccountChoice(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncAccountSelection.Selected)) Repository.Requery.Invalidate();
    }

    // The user chose another remote or upstream branch: another remote drops the old one's choices; either reads again.
    public void Choose(string? remote, SyncBranch? branch)
    {
        var upstream = remote == Target.RemoteName ? Upstream with { Chosen = branch } : SyncBranchChoice.None;
        (Target, Upstream) = (Target with { RemoteName = remote }, upstream);
        ChoiceChanged?.Invoke();
        StartRead();
    }

    // Git configuration can change without moving the snapshot: the shown values stay while the read confirms them.
    public void Reread()
    {
        if (IsReading) return;
        Begin();
    }

    public Task<GitWriteResult?> FetchAsync() => SynchronizeAsync(SyncAction.Fetch, CanSynchronize);
    public Task<GitWriteResult?> PullAsync() => SynchronizeAsync(SyncAction.Pull, CanPull);
    public Task<GitWriteResult?> ForcePushAsync() => SynchronizeAsync(SyncAction.ForcePush, CanForcePush);
    public Task<GitWriteResult?> SaveAccountAsync() => SynchronizeAsync(SyncAction.SelectAccount, CanSaveAccount);

    // Push after a merge sends the checked-out branch to the upstream it tracks, read again for it: null when it tracks
    // none the remote has, or the branch cannot send commits now.
    public async Task<RepositorySyncState?> ReadTrackedAsync()
    {
        if (reader is null || Repository.Snapshot is not { TrackingRemote: { } remote, UpstreamBranchRef: { } upstream } snapshot) return null;
        var state = await reader.ReadSyncAsync(snapshot, remote, upstream, CancellationToken.None);
        return state is { HasUpstream: true } && state.CanTransfer(snapshot) ? state : null;
    }

    // A remote outside the target, as one just added, fetched once: its state read for it from the configuration as it is
    // now, then fetched the way the target is; null when Trag cannot exchange with it, a refusal when its state cannot be read.
    public async Task<GitWriteResult?> FetchRemoteAsync(string remote)
    {
        if (reader is null || Repository.Snapshot is not { } snapshot) return null;
        RepositorySyncState? state;
        try { state = await reader.ReadSyncAsync(snapshot, remote, null, CancellationToken.None); }
        catch (Exception) { return GitWriteResult.Rejected(Failures.FetchNotStarted); }
        return state is { CanSynchronize: true } ? await Repository.Writer.RunAsync(expected => new SyncRequest(expected, state, SyncAction.Fetch)) : null;
    }

    public Task<GitWriteResult?> PushToAsync(RepositorySyncState tracked) => Repository.Writer.RunAsync(expected => new SyncRequest(expected, tracked, SyncAction.Push));

    // Runs beside the shown state rather than as a write, for the target that was read, with its account; null when it
    // cannot run. A fetch that succeeded is recorded here; the caller reads the target again once it knows the fetch was
    // still wanted.
    public async Task<GitWriteResult?> FetchQuietlyAsync(CancellationToken ct)
    {
        if (!CanFetchQuietly) return null;
        var target = Overview.State!;
        var result = await reader!.FetchQuietlyAsync(Repository.Snapshot!, target, Overview.Accounts.Selected?.Id, ct);
        if (result is { Outcome: GitWriteOutcome.Succeeded }) RecordFetch(target.Remote.Name);
        return result;
    }

    public Task<GitWriteResult?> PushAsync()
    {
        if (!CanPush) return RepositoryWriter.NotRun;
        // Publishing a new remote branch also records its upstream.
        if (Overview.State?.Branch is { IsNew: true } target && Target.Remote is { } remote && Repository.Snapshot is { Branch: { } branch } snapshot)
            return PublishAsync(new(snapshot, PublishOperation.PushTo(remote.Name, branch, target.Display)));
        return SynchronizeAsync(SyncAction.Push, true);
    }

    // The publish the dialog confirmed, built now over the shown snapshot with the account and owner the user chose; the
    // dialog runs it later. Null without a branch to publish.
    public ConfirmedPublish? ConfirmPublish(PublishInput input, string? accountId, string? ownerId) =>
        Repository.Snapshot is { Branch: { } branch } expected ? new(this, new(expected, input.ToOperation(branch, accountId, ownerId))) : null;

    // A publish, from the dialog or from Push: the status line follows it, and so does whatever the caller shows it on.
    internal Task<GitWriteResult?> PublishAsync(PublishRequest request, Action<string>? progress = null) =>
        Repository.Writer.RunAsync(request, progress);

    // The application goes: the read stops, and a late answer of it changes neither the panel nor the status bar.
    public void Clear()
    {
        read.Clear();
        Follow(SyncTargetSelection.Empty, SyncBranchChoice.None);
        Overview = SyncOverview.Empty();
        statusBar.SyncOperationText = "";
        statusBar.SyncDetails = "";
    }

    // The read's value is the panel; its message is an empty panel for the chosen remote that says why.
    private void FollowRead(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(read.Value): Overview = read.Value; break;
            case nameof(read.Message) when read.Message.Length != 0: Overview = SyncOverview.Empty(Target.Remote) with { Message = read.Message }; break;
            case nameof(read.IsBusy):
                OnPropertyChanged(nameof(IsReading));
                if (!following) Repository.Requery.Invalidate();
                break;
        }
    }

    // A new target starts over; a fetch that moved a remote-tracking ref (by Trag or any other program) reads the same
    // target again, keeping what it shows meanwhile. The upstream choice survives while the target keeps its branches.
    private void Apply(RepositorySnapshot snapshot)
    {
        var next = Target.WithSnapshot(snapshot);
        var restart = Target.Remote != next.Remote || Target.NeedsRead(snapshot);
        var moved = !restart && Target.RemotesMoved(snapshot);
        readOnLastApply = restart || moved;
        following = true;
        try
        {
            Follow(next, Target.KeepsBranches(next) ? Upstream : SyncBranchChoice.None);
            if (restart) StartRead();
            else if (moved) Reread();
        }
        finally { following = false; }
    }

    // A changed target cannot keep counts or credentials from the previous one.
    private void StartRead()
    {
        read.Cancel();
        Overview = SyncOverview.Empty(Target.Remote);
        Begin();
    }

    // An explicit refresh reads the target again, unless the snapshot it applied read it already.
    internal void FollowRefresh()
    {
        if (!readOnLastApply) Reread();
    }

    private void Begin()
    {
        if (reader is null || Target.Snapshot is not { } snapshot || Target.Remote is not { } remote || Repository.IsWriting) return;
        var branch = Upstream.Chosen?.SourceRef;
        read.LoadAsync(async ct =>
        {
            var state = await reader.ReadSyncAsync(snapshot, remote.Name, branch, ct);
            if (state is null) return null;
            // A superseded or interrupted read must not touch the upstream choice.
            ct.ThrowIfCancellationRequested();
            Upstream = new(state.Choices, state.Branch);
            ChoiceChanged?.Invoke();
            return SyncOverview.From(snapshot, state, LastFetchOf(remote.Name), Repository.Time.GetUtcNow());
        }, interim: Overview.Reading());
    }

    private void Follow(SyncTargetSelection target, SyncBranchChoice upstream)
    {
        (Target, Upstream) = (target, upstream);
        TargetFollowed?.Invoke();
    }

    private async Task<GitWriteResult?> SynchronizeAsync(SyncAction action, bool allowed)
    {
        if (!allowed || Overview.State is not { } target) return null;
        var account = Overview.Accounts.Selected?.Id;
        var result = await Repository.Writer.RunAsync(expected => new SyncRequest(expected, target, action, account));
        // A fetch brought the remote's refs; so did a pull, whether its merge went through or was left to resolve.
        if (action is SyncAction.Fetch or SyncAction.Pull && result is { Started: true })
            RecordFetch(target.Remote.Name);
        return result;
    }

    private DateTimeOffset? LastFetchOf(string remote) => fetched.TryGetValue(remote, out var at) ? at : null;

    // The remote was fetched now: the shown overview says so at once, whichever read is in flight, and the automatic
    // fetch hears of it.
    private void RecordFetch(string remote)
    {
        var at = Repository.Time.GetUtcNow();
        fetched[remote] = at;
        if (Overview.State?.Remote.Name == remote) Overview = Overview.Fetched(at);
        Fetched?.Invoke(at);
    }
}
