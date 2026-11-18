using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.History;

public sealed class HistoryViewModelTests
{
    // More read below the chosen commit leaves it chosen with its details, and text typed into the search box outside
    // Search is no query for the history read.
    [Fact]
    public async Task WhenMoreIsReadBelowTheChosenCommit_ShouldKeepItChosenAndReadTheWholeHistory()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        var chosen = model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;
        model.History.SearchQuery = "draft search";

        await model.History.LoadMoreHistoryCommand.ExecuteAsync(null);

        Assert.Equal((2, ""), (model.History.Page.Commits.Count, history.LastQuery));
        Assert.Same(chosen, model.History.SelectedCommit);
        Assert.NotNull(model.History.Details.Panel.Details);
    }

    // The user's own Fetch: the snapshot it brings arrives while the write still runs, so the page is read again in
    // place once it ended, and the chosen commit stays chosen with its details.
    [Fact]
    public async Task WhenTheUserFetches_ShouldKeepTheChosenCommitAndReadThePageAgainInPlace()
    {
        var h = Harness();
        h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;
        var (calls, details) = (h.History.PageCalls, h.History.DetailCalls);
        h.Reader.Snapshot = h.Synced() with { RemoteRefs = "moved" };

        await model.Sync.FetchCommand.ExecuteAsync(null);
        await model.History.HistoryCompletion;

        Assert.Equal((calls + 1, details), (h.History.PageCalls, h.History.DetailCalls));
        Assert.Equal(model.History.Page.Commits[0].Id, model.History.SelectedCommit!.Id);
        Assert.NotNull(model.History.Details.Panel.Details);
    }

    // A search's matches are not one line of history, so the graph column is left out while Search is shown.
    [Fact]
    public async Task WhenSearchIsShown_ShouldLeaveTheGraphOutUntilHistoryIsShownAgain()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var changed = new List<string?>();
        model.History.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Navigator.ShowSearchCommand.Execute(null);
        await model.History.HistoryCompletion;
        var search = (model.History.ShowsGraph, changed.Contains(nameof(model.History.ShowsGraph)));
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;

        Assert.Equal((false, true), search);
        Assert.True(model.History.ShowsGraph);
    }

    [Fact]
    public async Task WhenASearchTooLongToRunOvertakesAHeldOne_ShouldShowItsMessageAtOnceAndIgnoreTheLatePage()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowSearchCommand.Execute(null);
        await model.History.HistoryCompletion;
        history.Gate.IsClosed = true;
        model.History.SearchQuery = "old";
        var old = model.History.HistoryCompletion;
        await history.Gate.Arrival(2);
        model.History.SearchQuery = new string('x', 1025);
        Assert.True(model.History.HistoryCompletion.IsCompletedSuccessfully);
        Assert.False(model.History.IsHistoryBusy);
        Assert.Empty(model.History.Page.Commits);
        var invalid = model.History.Page;
        Assert.NotEmpty(invalid.Message);
        history.Gate.Open();
        await old;
        Assert.Same(invalid, model.History.Page);
        Assert.Equal(2, history.PageCalls);
    }

    // The list scrolled on to its end time after time reads each next page once and shows it below the ones before;
    // closing the repository drops them all.
    [Fact]
    public async Task WhenTheListIsScrolledOnThenTheRepositoryCloses_ShouldReadEachNextPageBelowAndDropThemAllOnClose()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;

        for (var i = 0; i < 6; i++) await model.History.LoadMoreHistoryCommand.ExecuteAsync(null);

        Assert.Equal((7, 6, 7), (model.History.Page.Commits.Count, model.History.Page.Number, history.PageCalls));
        Assert.Equal("7 commits", model.History.Page.CountText);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Empty(model.History.Page.Commits); Assert.Empty(model.History.Details.Panel.Files);
    }

    [Fact]
    public async Task WhenTheWholeHistoryIsShown_ShouldOfferNoMoreToRead()
    {
        var h = Harness();
        h.History.Pages = (subject, page) => Page(subject, page) with { HasMore = false };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;

        Assert.False(model.History.LoadMoreHistoryCommand.CanExecute(null));
    }
    [Fact]
    public async Task WhenANewSearchOvertakesAHeldOne_ShouldClearTheResultsAtOnceAndIgnoreTheLateResponse()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        history.Gate.IsClosed = true;
        model.History.SearchQuery = "old"; var old = model.History.HistoryCompletion; await history.Gate.Arrival(2);
        history.Gate.IsClosed = false; // the query after it answers at once, so the held one has to be overtaken
        model.History.SearchQuery = "current";
        Assert.Empty(model.History.Page.Commits); Assert.True(history.Gate.TokenAt(2).IsCancellationRequested);
        await model.History.HistoryCompletion;
        Assert.Equal("current", Assert.Single(model.History.Page.Commits).Subject);
        history.Gate.Open(); await old;
        Assert.Equal("current", Assert.Single(model.History.Page.Commits).Subject);
        Assert.False(model.History.IsHistoryBusy);
    }
    [Fact]
    public async Task WhenHeadMovesOnRefresh_ShouldStartOverAndDropTheSelectedCommitsDetails()
    {
        var h = Harness(); var reader = h.Reader; var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        Assert.NotEmpty(model.History.Details.Panel.Body);
        reader.Snapshot = reader.Snapshot with { Head = "new-head" };
        await model.RefreshCommand.ExecuteAsync(null); await model.History.HistoryCompletion;
        Assert.Null(model.History.SelectedCommit); Assert.Empty(model.History.Details.Panel.Body); Assert.Equal(0, model.History.Page.Number);
    }
    [Fact]
    public async Task WhenTheUserCancelsARunningSearch_ShouldShowItCanceledWithNoLateResults()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        history.Gate.IsClosed = true;
        model.History.SearchQuery = "cancel"; await history.Gate.Arrival(2);
        model.CancelCommand.Execute(null); Assert.True(history.Gate.Token.IsCancellationRequested);
        history.Gate.Cancel(); await model.History.HistoryCompletion;
        Assert.Empty(model.History.Page.Commits); Assert.False(model.History.IsHistoryBusy); Assert.Contains("canceled", model.History.Page.Message);
    }
    // The commit-diff pane keeps its own request.
    [Fact]
    public async Task WhenAnotherCommitFileOvertakesAHeldDiffRead_ShouldNeverShowItsLateResult()
    {
        var h = Harness(); var history = h.History;
        history.Details = id => new(Snapshots.Commit(id, "subject"), "Commit body", [File("a.txt"), File("b.txt")]);
        history.Diff = (_, file, _) => new FileDiff([], [new('+', file.Path.Display)], DiffContentKind.Text, "");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        var details = model.History.Details;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await details.Completion;
        history.Gate.IsClosed = true;
        details.Panel.SelectedFile = details.Panel.Files[0]; var old = details.Diff.Completion; await history.Gate.Arrival(3);
        Assert.True(details.Diff.IsBusy);
        history.Gate.IsClosed = false; // the read that overtakes it answers at once
        details.Panel.SelectedFile = details.Panel.Files[1]; await details.Diff.Completion;
        Assert.True(history.Gate.TokenAt(3).IsCancellationRequested);
        Assert.Equal("b.txt", Assert.Single(details.Diff.Document.Lines).Text);
        history.Gate.Open(); await old;
        Assert.Equal("b.txt", Assert.Single(details.Diff.Document.Lines).Text); Assert.False(details.Diff.IsBusy);
    }

    // The read canceled here follows an overtaken one, as in the flow it was split from, so it is the pane's third.
    [Fact]
    public async Task WhenTheUserCancelsACommitDiffRead_ShouldShowItCanceledAndThePaneNoLongerBusy()
    {
        var h = Harness(); var history = h.History;
        history.Details = id => new(Snapshots.Commit(id, "subject"), "Commit body", [File("a.txt"), File("b.txt")]);
        history.Diff = (_, file, _) => new FileDiff([], [new('+', file.Path.Display)], DiffContentKind.Text, "");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        var details = model.History.Details;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await details.Completion;
        await OvertakeTheFirstFilesReadAsync(history, details);

        history.Gate.IsClosed = true;
        details.Panel.SelectedFile = details.Panel.Files[0]; await history.Gate.Arrival(5);
        Assert.True(details.Diff.IsBusy); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null); Assert.True(history.Gate.Token.IsCancellationRequested);
        history.Gate.Cancel(); await details.Diff.Completion;
        Assert.False(details.Diff.IsBusy); Assert.Equal("Commit diff canceled.", details.Diff.Document.Message); Assert.Empty(details.Diff.Document.Lines);
    }
    // The first file's read is held, the second file's overtakes it, and then the held one is let go.
    private static async Task OvertakeTheFirstFilesReadAsync(FakeHistoryReader history, Trag.ViewModels.History.CommitDetailsViewModel details)
    {
        history.Gate.IsClosed = true;
        details.Panel.SelectedFile = details.Panel.Files[0]; var old = details.Diff.Completion; await history.Gate.Arrival(3);
        history.Gate.IsClosed = false;
        details.Panel.SelectedFile = details.Panel.Files[1]; await details.Diff.Completion;
        history.Gate.Open(); await old;
    }
    private static FileDiffMetadata File(string path) => new(Snapshots.Path(path), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheDetailsAreClearedDuringACommitReadThatIgnoresCancel_ShouldShowNoCommitAndNotBusy(bool fail)
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        history.Gate.IsClosed = true;
        var loading = model.History.Details.Load(Snapshots.Commit("old", "old"));
        await history.Gate.Started;
        model.History.Details.Clear();
        Assert.True(history.Gate.Token.IsCancellationRequested);
        if (fail) history.Gate.Fail(new IOException("late failure"));
        else history.Gate.Open();
        await loading;
        Assert.Equal("Select a commit to see its details.", model.History.Details.Panel.Heading);
        Assert.Null(model.History.Details.Panel.Details);
        Assert.False(model.History.Details.IsBusy);
    }

    [Fact]
    public async Task WhenAnotherCommitReplacesTheShownOne_ShouldDiffAgainstItsParentAndIgnoreTheOldPanelsSelection()
    {
        var h = Harness(); var history = h.History;
        history.Details = id => new(Snapshots.Commit(id, id) with { Parents = [id + "-parent"] }, id, [File("a.cs"), File("b.cs")]);
        history.Diff = (commit, file, _) => new([file], [new('+', commit.Parents[0])], DiffContentKind.Text, "");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var details = model.History.Details;
        await details.Load(Snapshots.Commit("first", "first"));
        var old = details.Panel;
        old.SelectedFile = old.Files[0];
        await details.Diff.Completion;
        await details.Load(Snapshots.Commit("second", "second"));
        details.Panel.SelectedFile = details.Panel.Files[0];
        await details.Diff.Completion;
        var document = details.Diff.Document;
        var calls = history.Gate.Entries;
        old.SelectedFile = old.Files[1];
        Assert.Equal(calls, history.Gate.Entries);
        Assert.Same(document, details.Diff.Document);
        Assert.Equal("second-parent", Assert.Single(document.Lines).Text);
    }

    [Fact]
    public async Task WhenTheScopeChangesWhileTheNextPageIsReading_ShouldDropThatPageAndShowTheCurrentBranchFromTheStart()
    {
        var h = Harness();
        var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        history.Gate.IsClosed = true;
        var old = model.History.LoadMoreHistoryCommand.ExecuteAsync(null);
        await history.Gate.Arrival(2);
        history.Gate.IsClosed = false;
        model.History.ToggleHistoryScopeCommand.Execute(null);
        await model.History.HistoryCompletion;
        var current = model.History.Page;
        history.Gate.Open();
        await old;
        Assert.Same(current, model.History.Page);
        Assert.False(history.LastAllBranches);
        Assert.Equal(0, current.Number);
        Assert.StartsWith("Current branch", current.Message);
    }
    // Copy SHA copies the chosen commit, which its menu chooses; a refresh that drops the choice while the menu is open
    // leaves nothing to copy.
    [Fact]
    public async Task WhenTheChosenCommitIsDroppedBeforeCopySha_ShouldCopyNothing()
    {
        await using var model = Harness().Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        var host = new RecordingHost();
        model.Host = host;
        model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;

        model.History.CopyCommitIdCommand.Execute(null);
        model.History.SelectedCommit = null;
        model.History.CopyCommitIdCommand.Execute(null);

        Assert.Equal([("copy", Snapshots.HeadId)], host.Calls);
    }

    private static HistoryPage Page(string subject, int page) => new([Snapshots.Commit(Snapshots.HeadId, subject)], true, page);
    // A repository at "head" whose history answers every page with one commit, its subject the query that asked for
    // it, and always one more page.
    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        h.History.Pages = Page;
        h.History.Details = _ => new(Page("subject", 0).Commits[0], "Commit body", []);
        return h;
    }
}
