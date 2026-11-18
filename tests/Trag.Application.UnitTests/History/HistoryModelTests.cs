using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Stash;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.History;

// The repository's history over the real workspace and fake Git: when the feed reads, what it keeps, how a new snapshot
// moves it, and the commit the pane shows.
public sealed class HistoryModelTests
{
    private static readonly HistoryFilter AllBranches = new("", true, false);

    // Two commits on every page, and always one more page.
    private static FakeHistoryReader Reader() => new()
    {
        Pages = (_, page) => new([Snapshots.Commit(Snapshots.Id((char)('a' + page)), "Page " + page), Snapshots.Commit(Snapshots.Id('z'), "Root")], true, page)
    };

    [Fact]
    public async Task WhenTheFeedIsNotShown_ShouldNotRead()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;

        history.Restart(AllBranches); await history.Completion;

        Assert.Equal(0, pages.PageCalls); Assert.Same(ShownHistoryPage.Empty, history.Page);
    }

    [Fact]
    public async Task WhenTheFeedIsShown_ShouldReadTheFirstPage()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;

        history.IsShown = true;
        history.Restart(AllBranches); await history.Completion;

        var first = history.Page;
        Assert.Equal((1, 2, "All branches · newest commits first"), (pages.PageCalls, first.Commits.Count, first.Message));
    }

    // A commit asked for by its id is chosen once the search read for it is in, when the shown page has it.
    [Fact]
    public async Task WhenACommitIsAskedForById_ShouldChooseItOnceThePageHasIt()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;
        history.IsShown = true;

        history.Restart(AllBranches, debounce: true);
        await history.SelectAsync(Snapshots.Id('z'));
        Assert.Equal(Snapshots.Id('z'), history.Selected?.Id);

        history.Restart(AllBranches);
        await history.SelectAsync(Snapshots.Id('y'));
        Assert.Null(history.Selected);
    }

    [Fact]
    public async Task WhenGitDoesNotAnswerInTime_ShouldSaySoInsteadOfCanceled()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;
        pages.Pages = (_, _) => throw new TimeoutException();

        history.IsShown = true;
        history.Restart(AllBranches); await history.Completion;

        Assert.Equal(Failures.TimedOut, history.Page.Message);
    }

    // Scrolling on reads the next page each time and adds its commits below the ones shown, with their rows in the graph.
    [Fact]
    public async Task WhenTheUserScrollsOn_ShouldReadTheNextPageAndAddItsCommitsBelow()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;
        history.IsShown = true;
        history.Restart(AllBranches); await history.Completion;

        await history.LoadMoreAsync();
        await history.LoadMoreAsync();

        Assert.Equal((3, 2), (pages.PageCalls, history.Page.Number));
        Assert.Equal(["Page 0", "Root", "Page 1", "Root", "Page 2", "Root"], history.Page.Commits.Select(commit => commit.Subject));
        Assert.NotNull(history.Page.Graph.For(Snapshots.Id('c')));
    }

    [Fact]
    public async Task WhenMoreIsLoaded_ShouldKeepTheChosenCommitAndItsDetails()
    {
        var (workspace, repository, _, pages) = await SelectedAsync(() => { });
        await using var _ = workspace;
        var history = repository.History;

        await history.LoadMoreAsync();

        Assert.Equal(4, history.Page.Commits.Count);
        Assert.Equal(Snapshots.Id('z'), history.Selected!.Id); Assert.Equal(1, pages.DetailCalls);
    }

    [Fact]
    public async Task WhenTheWholeHistoryIsShown_ShouldReadNoMore()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;
        pages.Pages = (_, page) => new([Snapshots.Commit(Snapshots.Id('a'), "Only")], false, page);
        history.IsShown = true;
        history.Restart(AllBranches); await history.Completion;

        await history.LoadMoreAsync();

        Assert.Equal(1, pages.PageCalls);
    }

    // A fetch that moved a remote ref reads every commit shown again, in one read, so the list keeps its length and
    // the user's place in it.
    [Fact]
    public async Task WhenAFetchMovedARemoteRefAfterMoreWasLoaded_ShouldReadEveryShownPageAgainInOneRead()
    {
        var (workspace, repository, reader, pages) = await SelectedAsync(() => { });
        await using var _ = workspace;
        var history = repository.History;
        await history.LoadMoreAsync();

        await FetchedAsync(workspace, repository, reader);

        Assert.Equal((3, 0, 2), (pages.PageCalls, pages.LastPageIndex, pages.LastPages));
        Assert.Equal((4, 1), (history.Page.Commits.Count, history.Page.Number));
        Assert.Equal(Snapshots.Id('z'), history.Selected!.Id);
    }

    [Fact]
    public async Task WhenAFetchMovedARemoteRef_ShouldReadTheShownPageAgainInPlaceAndKeepTheChoice()
    {
        var restarts = 0;
        var (workspace, repository, reader, pages) = await SelectedAsync(() => restarts++);
        await using var _ = workspace;
        var history = repository.History;
        Assert.Equal(1, restarts); Assert.Equal(Snapshots.Id('z'), history.Details.Commit.Value.Heading);

        await FetchedAsync(workspace, repository, reader);

        Assert.Equal((2, 1), (pages.PageCalls, restarts));
        Assert.Equal(Snapshots.Id('z'), history.Selected!.Id); Assert.Equal(1, pages.DetailCalls);
    }

    // A fetch moved a remote ref first, which read the page again without starting over.
    [Fact]
    public async Task WhenHeadMoves_ShouldStartOverAtTheNewHeadAndDropTheChoice()
    {
        var restarts = 0;
        var (workspace, repository, reader, pages) = await SelectedAsync(() => restarts++);
        await using var _ = workspace;
        var history = repository.History;
        await FetchedAsync(workspace, repository, reader);

        reader.Snapshot = reader.Snapshot with { Head = Snapshots.Id('c') };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await history.Completion;

        Assert.Equal((3, 2), (pages.PageCalls, restarts));
        Assert.Null(history.Selected); Assert.Null(history.Details.Subject); Assert.Same(ShownCommit.None, history.Details.Commit.Value);
        Assert.Equal(Snapshots.Id('c'), pages.LastHead);
    }

    // The user's own fetch: the snapshot it brings arrives while the write still runs, so the page is read again in
    // place once the write ended, and the chosen commit keeps its details.
    [Fact]
    public async Task WhenTheUsersFetchMovedARemoteRef_ShouldReadTheShownPageAgainInPlaceOnceItEndedAndKeepTheChoice()
    {
        var restarts = 0;
        var (workspace, repository, reader, pages) = await SelectedAsync(() => restarts++, new FakeGitWriter());
        await using var _ = workspace;
        var history = repository.History;
        reader.Snapshot = reader.Snapshot with { RemoteRefs = "fetched" };

        await repository.Writer.RunAsync(expected => new SyncRequest(expected, Origin(), SyncAction.Fetch)); await history.Completion;

        Assert.Equal((2, 1, 1), (pages.PageCalls, restarts, pages.DetailCalls));
        Assert.Equal(Snapshots.Id('z'), history.Selected!.Id); Assert.Equal(Snapshots.Id('z'), history.Details.Commit.Value.Heading);
    }

    // A tag shows on its commit, so a tag the user created reads the shown page again in place, like a fetch, and the
    // chosen commit keeps its details.
    [Fact]
    public async Task WhenTheUsersTagWriteAddedATag_ShouldReadTheShownPageAgainInPlaceAndKeepTheChoice()
    {
        var restarts = 0;
        var (workspace, repository, reader, pages) = await SelectedAsync(() => restarts++, new FakeGitWriter());
        await using var _ = workspace;
        var history = repository.History;
        reader.Snapshot = reader.Snapshot with { TagRefs = "tagged" };

        await repository.Writer.RunAsync(expected => new TagRequest(expected, new("v1.0", Snapshots.Id('z')))); await history.Completion;

        Assert.Equal((2, 1, 1), (pages.PageCalls, restarts, pages.DetailCalls));
        Assert.Equal(Snapshots.Id('z'), history.Selected!.Id);
    }

    // A write that moved no ref (a stash set aside, an account saved) leaves the feed and the choice as they are.
    [Fact]
    public async Task WhenAWriteMovedNothingInTheHistory_ShouldKeepThePageAndTheChoiceWithoutReading()
    {
        var restarts = 0;
        var (workspace, repository, _, pages) = await SelectedAsync(() => restarts++, new FakeGitWriter());
        await using var _ = workspace;
        var history = repository.History;
        var page = history.Page;

        await repository.Writer.RunAsync(expected => new StashRequest(expected, new StashOperation("wip", false), StashAction.Push)); await history.Completion;

        Assert.Equal((1, 1), (pages.PageCalls, restarts));
        Assert.Same(page, history.Page); Assert.Equal(Snapshots.Id('z'), history.Selected!.Id);
    }

    // A write that moved HEAD (a commit) starts the feed over, once the write ended.
    [Fact]
    public async Task WhenAWriteMovedTheHead_ShouldStartOverOnceItEnded()
    {
        var restarts = 0;
        var (workspace, repository, reader, pages) = await SelectedAsync(() => restarts++, new FakeGitWriter());
        await using var _ = workspace;
        var history = repository.History;
        reader.Snapshot = reader.Snapshot with { Head = Snapshots.Id('c') };

        await repository.Writer.RunAsync(expected => new StashRequest(expected, new StashOperation("wip", false), StashAction.Push)); await history.Completion;

        Assert.Equal((2, 2), (pages.PageCalls, restarts));
        Assert.Null(history.Selected); Assert.Equal(Snapshots.Id('c'), pages.LastHead); Assert.Equal(2, history.Page.Commits.Count);
    }

    // A write that begins while a page is being read drops that read; the page is read again once the write ended.
    [Fact]
    public async Task WhenAWriteInterruptsAPageRead_ShouldReadThePageAgainOnceItEnded()
    {
        var (workspace, repository, _, pages) = await SelectedAsync(() => { }, new FakeGitWriter());
        await using var _ = workspace;
        var history = repository.History;
        pages.Gate.IsClosed = true;
        history.Restart(AllBranches);
        await pages.Gate.Arrival(2);
        Assert.True(history.IsReading);

        await repository.Writer.RunAsync(expected => new StashRequest(expected, new StashOperation("wip", false), StashAction.Push));
        pages.Gate.Open(); await history.Completion;

        Assert.Equal(3, pages.PageCalls); Assert.Equal(2, history.Page.Commits.Count); Assert.False(history.IsReading);
    }

    // A branch made without switching to it, deleted, renamed or moved with branch -f, or a commit made in another
    // worktree, moves only local refs, which the page shows as labels and the range of every branch follows.
    [Fact]
    public async Task WhenOnlyLocalRefsChange_ShouldReadTheShownPageAgain()
    {
        var pages = Reader();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: pages);
        var repository = new Repository(Services(workspace));
        repository.History.IsShown = true;
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        repository.Apply(opened); await repository.History.Completion;
        repository.Apply(opened with { LocalRefs = "moved" }); await repository.History.Completion;
        Assert.Equal(2, pages.PageCalls);
    }

    // branch --set-upstream-to changes only the configuration and moves no ref, but the current branch is read together
    // with its upstream, so the shown page is read again with the new one.
    [Fact]
    public async Task WhenTheUpstreamChangesOnlyInConfig_ShouldReadTheCurrentScopeAgainWithTheNewUpstream()
    {
        var pages = Reader();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: pages);
        var repository = new Repository(Services(workspace));
        repository.History.IsShown = true;
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        repository.Apply(opened); repository.History.Restart(new("", false, false)); await repository.History.Completion;
        var calls = pages.PageCalls;

        repository.Apply(opened with { Upstream = "origin/other", UpstreamHead = Snapshots.Id('u') }); await repository.History.Completion;

        Assert.Equal((calls + 1, Snapshots.Id('u')), (pages.PageCalls, pages.LastUpstream));
    }

    // A page Git could not hand over (a commit message larger than a history record may be, an output Trag cannot read)
    // says so in general words, naming a very large message as a possible reason.
    [Fact]
    public async Task WhenThePageCannotBeRead_ShouldSayTheHistoryCouldNotBeReadAndThatALargeMessageMayBeWhy()
    {
        var (workspace, history, pages) = await OpenAsync();
        await using var _ = workspace;
        pages.Pages = (_, _) => throw new FormatException("Incomplete Git record.");

        history.IsShown = true;
        history.Restart(AllBranches); await history.Completion;

        Assert.Equal(HistoryText.Failed, history.Page.Message);
        Assert.Contains("unusually large commit messages", history.Page.Message, StringComparison.Ordinal);
    }

    // A snapshot that changed only files moved no ref: the shown page stays as it was read.
    [Fact]
    public async Task WhenOnlyFilesChanged_ShouldNotReadTheShownPageAgain()
    {
        var pages = Reader();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: pages);
        var repository = new Repository(Services(workspace));
        repository.History.IsShown = true;
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        repository.Apply(opened); await repository.History.Completion;
        repository.Apply(opened with { Files = [Snapshots.Modified("a.txt")] }); await repository.History.Completion;
        Assert.Equal(1, pages.PageCalls);
    }

    [Fact]
    public async Task WhenNothingIsOpen_ShouldHandOutNoFileDiff()
    {
        var file = ChangedFile();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: Reader());
        var details = new Repository(Services(workspace)).History.Details;
        Assert.Null(details.DiffOf(new(Snapshots.Commit("x", "x"), "", [file]), file));
    }

    // The pane shows any commit, a stash among them, before its details are read.
    [Fact]
    public async Task WhenTheUserShowsACommit_ShouldNameItAtOnce()
    {
        var (workspace, details, _) = await DetailsAsync();
        await using var _ = workspace;
        var shown = 0;
        details.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CommitDetailsModel.Subject)) shown++; };

        var loading = details.Show(Snapshots.Commit(Snapshots.Id('s'), "Stash"));

        Assert.Equal(1, shown); Assert.NotNull(details.Subject);
        await loading;
    }

    [Fact]
    public async Task WhenTheShownCommitsDetailsAreRead_ShouldHeadThemWithItsId()
    {
        var (workspace, details, _) = await DetailsAsync();
        await using var _ = workspace;

        await details.Show(Snapshots.Commit(Snapshots.Id('s'), "Stash"));

        Assert.Equal(Snapshots.Id('s'), details.Commit.Value.Heading);
    }

    // The pane hands the reader of one file's diff out, which reads over the shown snapshot.
    [Fact]
    public async Task WhenAFileOfTheShownCommitIsOpened_ShouldReadItsDiff()
    {
        var (workspace, details, file) = await DetailsAsync();
        await using var _ = workspace;
        await details.Show(Snapshots.Commit(Snapshots.Id('s'), "Stash"));
        var read = details.Commit.Value.Details!;

        var diff = await details.DiffOf(read, file)!(false, CancellationToken.None);

        Assert.Equal("+a.txt", Assert.Single(diff!.Lines).Text);
    }

    [Fact]
    public async Task WhenTheShownCommitIsCleared_ShouldShowNoCommit()
    {
        var (workspace, details, _) = await DetailsAsync();
        await using var _ = workspace;
        var shown = 0;
        details.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CommitDetailsModel.Subject)) shown++; };
        await details.Show(Snapshots.Commit(Snapshots.Id('s'), "Stash"));

        details.Clear();

        Assert.Equal(2, shown); Assert.Null(details.Subject); Assert.Same(ShownCommit.None, details.Commit.Value);
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());

    // The repository opened over fake Git; its feed is not shown yet.
    private static async Task<(RepositoryWorkspace Workspace, HistoryModel History, FakeHistoryReader Pages)> OpenAsync()
    {
        var pages = Reader();
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: pages);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (workspace, repository.History, pages);
    }

    // The feed shown, the repository opened, and the second commit of the first page selected with its details read.
    private static async Task<(RepositoryWorkspace Workspace, Repository Repository, FakeRepositoryReader Reader, FakeHistoryReader Pages)> SelectedAsync(
        Action restarted, FakeGitWriter? writer = null)
    {
        var pages = Reader();
        var reader = new FakeRepositoryReader();
        var workspace = new RepositoryWorkspace(reader, historyReader: pages, writer: writer);
        var repository = new Repository(Services(workspace));
        // As the session does: the snapshot a write verified is applied before the write ends.
        repository.Writer.ApplySnapshotAsync = snapshot => { repository.Apply(snapshot); return Task.CompletedTask; };
        var history = repository.History;
        history.IsShown = true;
        history.Restarting += restarted;
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await history.Completion;
        history.Selected = history.Page.Commits[1]; await history.Details.Commit.Completion;
        return (workspace, repository, reader, pages);
    }

    // A stash is shown in the pane with no commit chosen; choosing a commit, or a history that starts over, ends it.
    [Fact]
    public async Task WhenAStashIsPreviewed_ShouldShowItInPlaceOfTheCommitUntilACommitIsChosenOrTheFeedStartsOver()
    {
        var (workspace, repository, _, _) = await SelectedAsync(() => { });
        await using var _ = workspace;
        var history = repository.History;
        var stash = new StashEntry(Snapshots.Id('s'), 0, "On main: spike", "main", DateTimeOffset.UnixEpoch);

        await history.PreviewAsync(stash);
        Assert.Equal((stash, null), (history.PreviewedStash, history.Selected));
        Assert.Equal(Snapshots.Id('s'), history.Details.Subject?.Id);

        history.Selected = history.Page.Commits[0];
        Assert.Null(history.PreviewedStash);

        await history.PreviewAsync(stash);
        history.Restart(history.Filter);
        Assert.Null(history.PreviewedStash);
    }

    // A fetch moved a remote-tracking ref, and the refresh brought the snapshot.
    private static async Task FetchedAsync(RepositoryWorkspace workspace, Repository repository, FakeRepositoryReader reader)
    {
        reader.Snapshot = reader.Snapshot with { RemoteRefs = "fetched" };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.History.Completion;
    }

    // The repository opened; its history reads every commit's details as a stash that changed one file.
    private static async Task<(RepositoryWorkspace Workspace, CommitDetailsModel Details, FileDiffMetadata File)> DetailsAsync()
    {
        var pages = Reader();
        var file = ChangedFile();
        pages.Details = id => new(Snapshots.Commit(id, "Stash") with { Parents = [Snapshots.HeadId] }, "Body", [file]);
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), historyReader: pages);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (workspace, repository.History.Details, file);
    }

    private static FileDiffMetadata ChangedFile() =>
        new(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    // A sync target the writer accepts; what fake Git does with it is the writer's result.
    private static RepositorySyncState Origin() => new(new SyncRemote("origin", "fetch", "push", "config"), [], null, null, null, "");
}
