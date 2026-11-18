using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Operations;
using Trag.Application.Ports.Workspace;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.History;

// The repository's history as a feed of pages for one filter: the first page is read while a screen shows the feed, each
// next one when the screen asks for more, below the commits shown and with the graph going on from them; another HEAD
// starts over, and a fetch that moved a remote-tracking ref, or a tag that came, went or moved, reads every page shown
// again in place, keeping the chosen commit. A write pauses the reads; what its snapshots ask for, and a read it interrupted, wait for its end, and a write
// that moved nothing leaves the feed as it is. And the commit chosen in it, whose details the commit pane reads
// (Details), where a stash is shown too.
public sealed partial class HistoryModel : ObservableObject
{
    private enum Pending { None, Reload, Restart }
    // How a read shows: from the start, with the list emptied and nothing chosen meanwhile; again, every page shown read
    // in place, its commits staying until the new ones are in and the chosen commit chosen again; more, the next page
    // below the commits shown, the chosen commit left as it is.
    private enum Read { Start, Again, More }

    private readonly IHistoryWorkspace? reader;
    private readonly LatestRequest request = new();
    // The graph of the commits shown, which the next page read goes on with.
    private CommitGraphLayout layout = new();
    private RepositorySnapshot? historySnapshot;
    // What the write in progress left for its end: the strongest of what its snapshots and the read it interrupted asked for.
    private Pending pending;

    public HistoryModel(Repository repository, IHistoryWorkspace? reader)
    {
        Repository = repository;
        this.reader = reader;
        Details = new(repository, reader);
        repository.Cancels.Register(() => IsReading, request.Interrupt);
        repository.SnapshotApplied += (snapshot, _) => Apply(snapshot);
        repository.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Repository.IsWriting)) FollowWriting(); };
        // A stash shown that is gone leaves the pane: one dropped here at once, one gone elsewhere once the list says so.
        var stashes = repository.Stashes.List;
        stashes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(stashes.Value) && PreviewedStash is { } shown && stashes.Value.All(entry => entry.Id != shown.Id)) _ = PreviewAsync(null);
        };
        repository.Writer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepositoryWriter.LastWrite) && repository.Writer.LastWrite is
                { Request: StashRequest { Action: StashAction.Drop } drop, Result.Outcome: GitWriteOutcome.Succeeded } && drop.Stash.Id == PreviewedStash?.Id)
                _ = PreviewAsync(null);
        };
    }

    public Repository Repository { get; }
    public CommitDetailsModel Details { get; }
    // Whether a history or search screen shows the feed: it reads only then.
    public bool IsShown { get; set; }
    public HistoryFilter Filter { get; private set; } = new("", true, false);
    [ObservableProperty] public partial ShownHistoryPage Page { get; private set; } = ShownHistoryPage.Empty;
    [ObservableProperty] public partial HistoryCommit? Selected { get; set; }
    // A commit chosen to act on, with nothing else being written; the commands that act on it are asked again whenever
    // another one is chosen.
    public bool CanActOnSelected => Repository.HasRepository && !Repository.IsWriting && Selected is not null;
    [ObservableProperty] public partial bool IsReading { get; private set; }
    // A read that is no news to the user (another scope, the shown page read again) keeps Cancel out of sight.
    public bool IsQuiet { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    // The stash shown in the details pane in place of a commit; a commit chosen, or a feed that starts over, ends it.
    [ObservableProperty] public partial StashEntry? PreviewedStash { get; private set; }
    public event Action? Restarting;

    private bool CanRead => Repository.HasRepository && IsShown;

    // A page read starting or ending changes whether more may be read and whether Cancel has something to reach.
    partial void OnIsReadingChanged(bool value)
    {
        Repository.Requery.Invalidate();
        Repository.Cancels.NotifyChanged();
    }

    // The same commit chosen again, as a page read again hands back, keeps the details it shows.
    partial void OnSelectedChanged(HistoryCommit? oldValue, HistoryCommit? newValue)
    {
        Repository.Requery.Invalidate();
        if (newValue is not null) PreviewedStash = null;
        if (newValue is null || newValue.Id != oldValue?.Id) Details.Show(newValue);
    }

    // Chooses the commit with the id once the read in flight is in, when the shown page has it.
    public async Task SelectAsync(string id)
    {
        await Completion;
        if (Page.Commits.FirstOrDefault(commit => commit.Id == id) is { } found) Selected = found;
    }

    // The stash goes to the details pane as the commit it was made as, with no commit chosen; null ends the preview.
    public Task PreviewAsync(StashEntry? stash)
    {
        Selected = null;
        PreviewedStash = stash;
        return Details.Show(stash?.AsCommit());
    }

    // Starts the feed over for a filter: nothing shown and nothing chosen until its first page is in. A feed no screen
    // shows only empties.
    public void Restart(HistoryFilter filter, bool debounce = false, bool quiet = false)
    {
        Cancel();
        Completion = Task.CompletedTask;
        Page = ShownHistoryPage.Empty;
        ClearSelection();
        PreviewedStash = null;
        Restarting?.Invoke();
        Filter = filter;
        Start(debounce, quiet);
    }

    // The first page of the feed, for the filter: a write in progress holds it until its end.
    private void Start(bool debounce = false, bool quiet = false)
    {
        if (!CanRead)
            return;
        if (Repository.IsWriting)
        {
            pending = Pending.Restart;
            return;
        }
        if (Filter.ValidationMessage.Length != 0)
        {
            Page = Page with { Message = Filter.ValidationMessage };
            return;
        }
        Completion = LoadPageAsync(Read.Start, 0, debounce: debounce, quiet: quiet);
    }

    // The next page, read below the commits shown, once the whole history is not shown yet.
    public Task LoadMoreAsync() => Page.HasMore ? Completion = LoadPageAsync(Read.More, Page.Number + 1, quiet: true) : Completion;

    public void Cancel()
    {
        request.Cancel();
        Details.Cancel();
        IsReading = false;
    }

    // Another HEAD starts over; a fetch that moved a remote-tracking ref, a tag write, a local branch that moved
    // without HEAD, and another upstream, which the current branch is read with, read the shown page again in place.
    private void Apply(RepositorySnapshot snapshot)
    {
        var changed = !snapshot.HasSameHistory(historySnapshot);
        var moved = !changed && !(snapshot.HasSameRemotes(historySnapshot) && snapshot.HasSameTags(historySnapshot) && snapshot.HasSameLocals(historySnapshot)
            && snapshot.UpstreamHead == historySnapshot!.UpstreamHead);
        historySnapshot = snapshot;
        if (changed)
            Restart(Filter);
        else if (moved)
            Reload();
    }

    // Every page shown read again where it is, in one read: its commits stay on screen until the new ones are in, and the
    // chosen commit stays chosen when it is still there. A feed no screen shows reads when it is shown.
    private void Reload()
    {
        if (!CanRead || Filter.ValidationMessage.Length != 0) return;
        if (Repository.IsWriting)
        {
            if (pending == Pending.None) pending = Pending.Reload;
            return;
        }
        Cancel();
        Completion = LoadPageAsync(Read.Again, 0, pages: Page.Number + 1, quiet: true);
    }

    // A write begins: the reads in flight are dropped, and one that was reading is read again once the write ends. A
    // write ends: whatever its snapshots asked for runs now, the stronger of the two.
    private void FollowWriting()
    {
        if (Repository.IsWriting)
        {
            var interrupted = IsReading || Details.IsReading;
            Cancel();
            if (interrupted && pending == Pending.None) pending = Pending.Reload;
            return;
        }
        var held = pending;
        pending = Pending.None;
        if (held == Pending.Restart) Start();
        else if (held == Pending.Reload) Reload();
    }

    private void ClearSelection()
    {
        if (Selected is null) Details.Clear();
        else Selected = null;
    }

    private async Task LoadPageAsync(Read read, int number, int pages = 1, bool debounce = false, bool quiet = false)
    {
        if (historySnapshot is not { } expected || reader is null)
            return;
        using var scope = request.Begin();
        var applied = Filter;
        var keep = read == Read.Again ? Selected?.Id : null;
        IsQuiet = quiet;
        IsReading = true;
        if (read == Read.Start)
        {
            ClearSelection();
            Page = Page.Reading(applied.LoadingMessage);
        }
        try
        {
            if (debounce)
                await Task.Delay(300, scope.Token);
            var result = await reader.ReadHistoryAsync(expected, applied.ForPage(number, pages), scope.Token);
            if (!scope.IsLatest)
                return;
            scope.Token.ThrowIfCancellationRequested();
            if (result is null)
            {
                Page = Page with { Message = HistoryText.Changed };
                return;
            }

            if (read == Read.More)
            {
                Page = Page.Then(result, layout, applied, expected.Head is null);
                return;
            }
            layout = new();
            Page = ShownHistoryPage.From(result, layout, applied, expected.Head is null);
            if (read == Read.Again)
            {
                Selected = Page.Commits.FirstOrDefault(commit => commit.Id == keep);
                // The commit kept chosen keeps its details, unless the read of them was dropped meanwhile; that read is
                // the pane's own (Details.Commit.Completion), not the page's.
                if (Selected is { } kept && !Details.Shows(kept)) _ = Details.Show(kept);
            }
        }
        catch (OperationCanceledException)
        {
            if (scope.IsLatest)
                Page = Page with { Message = HistoryText.Canceled };
        }
        catch (TimeoutException)
        {
            if (scope.IsLatest)
                Page = Page with { Message = Failures.TimedOut };
        }
        catch (Exception)
        {
            if (scope.IsLatest)
                Page = Page with { Message = HistoryText.Failed };
        }
        finally
        {
            if (scope.IsLatest)
            {
                IsReading = false;
                IsQuiet = false;
            }
        }
    }
}
