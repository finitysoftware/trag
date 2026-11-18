using Trag.Domain.Blame;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.History;

// The blame view over the screen: a file blamed at a commit or in the working tree, a block's commit shown in the
// history or blamed again from the commit before it, and the screen under it left as it was once it closes.
public sealed class BlameViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly BlameCommit First = new(Snapshots.Id('a'), "Ana", Now.AddDays(-30), "Add a", Snapshots.Path("old.txt"));
    private static readonly BlameCommit Second = new(Snapshots.Id('b'), "Ana", Now.AddDays(-2), "Change a", Snapshots.Path("a.txt"))
    {
        Previous = Snapshots.Id('a'), PreviousPath = Snapshots.Path("old.txt")
    };
    private static readonly BlameCommit Local = new(new string('0', 40), "Not Committed Yet", Now, "Version of a.txt from a.txt", Snapshots.Path("a.txt"))
    {
        Previous = Snapshots.Id('b'), PreviousPath = Snapshots.Path("a.txt")
    };

    private static FileBlame Blame(params BlameCommit[] commits) => new([.. commits.Select((commit, index) => new BlameLine(index + 1, "line " + (index + 1), commit))]);

    private static async Task<(ShellHarness Harness, ShellViewModel Model)> OpenedAsync()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        h.History.Blames = (_, _) => Blame(First, First, Second, Local);
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Blame.OpenAsync("a.txt", null);
        return (h, model);
    }

    [Fact]
    public async Task WhenAFileIsBlamed_ShouldOpenOverTheScreenWithALineForEachOfItsLines()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;

        Assert.Equal((true, "a.txt", BlameText.Version(null)), (model.Blame.IsOpen, model.Blame.Path, model.Blame.Version));
        Assert.Equal([true, false, true, true], model.Blame.Rows.Select(row => row.StartsBlock));
        Assert.Equal((null, "a.txt"), h.History.LastBlame);
    }

    // The commit before a block's commit, with the name the file had there; the first commit of the file has none.
    [Fact]
    public async Task WhenABlockIsBlamedBeforeItsCommit_ShouldBlameTheFileAtThePreviousCommit()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        model.Blame.SelectedRow = model.Blame.Rows[0];
        Assert.False(model.Blame.BlameBeforeCommand.CanExecute(null));

        model.Blame.SelectedRow = model.Blame.Rows[2];
        await model.Blame.BlameBeforeCommand.ExecuteAsync(null);

        Assert.Equal((Snapshots.Id('a'), "old.txt"), h.History.LastBlame);
        Assert.Equal(("old.txt", BlameText.Version(Snapshots.Id('a')), null), (model.Blame.Path, model.Blame.Version, model.Blame.SelectedRow));
    }

    // Opened from a commit found by a search, the blame closes onto the search that finds this commit; a line not
    // committed has no commit to show.
    [Fact]
    public async Task WhenABlocksCommitIsShown_ShouldCloseAndFindTheCommitInTheHistory()
    {
        var (_, model) = await OpenedAsync();
        await using var __ = model;
        model.Navigator.ShowSearchCommand.Execute(null);
        await model.Blame.OpenAsync("a.txt", null);
        model.Blame.SelectedRow = model.Blame.Rows[3];
        Assert.False(model.Blame.ShowCommitCommand.CanExecute(null));

        model.Blame.SelectedRow = model.Blame.Rows[2];
        await model.Blame.ShowCommitCommand.ExecuteAsync(null);

        Assert.Equal((false, WorkspaceSection.Search, Snapshots.Id('b')), (model.Blame.IsOpen, model.Navigator.Section, model.History.SearchQuery));
    }

    // The history under the blame keeps its page: closing reads nothing again.
    [Fact]
    public async Task WhenTheBlameIsClosedOrAnotherSectionIsShown_ShouldLeaveTheScreenUnderItAsItWas()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        h.History.Blames = (_, _) => Blame(First);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        var (page, calls) = (model.History.Page, h.History.PageCalls);

        await model.Blame.OpenAsync("a.txt", Snapshots.Id('a'));
        Assert.True(model.Blame.IsOpen);
        model.Blame.CloseCommand.Execute(null);
        Assert.Equal((false, page, calls), (model.Blame.IsOpen, model.History.Page, h.History.PageCalls));

        await model.Blame.OpenAsync("a.txt", Snapshots.Id('a'));
        Assert.True(model.Blame.IsOpen);
        model.Navigator.ShowFileStatusCommand.Execute(null);
        Assert.False(model.Blame.IsOpen);
    }

    // The bar under the lines names the chosen block's commit.
    [Fact]
    public async Task WhenALineIsChosen_ShouldSayWhichCommitItsBlockIs()
    {
        var (_, model) = await OpenedAsync();
        await using var _ = model;
        var changed = new List<string?>();
        model.Blame.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Blame.SelectedRow = model.Blame.Rows[2];

        Assert.Equal(BlameText.Summary(Second), model.Blame.Summary);
        Assert.Contains(nameof(model.Blame.Summary), changed);
    }

    [Fact]
    public async Task WhenTheFileIsRead_ShouldBeBusyUntilItIsIn()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        h.History.Blames = (_, _) => Blame(First);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        h.History.Gate.IsClosed = true;
        var changed = new List<string?>();
        model.Blame.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var open = model.Blame.OpenAsync("a.txt", null);
        await h.History.Gate.Started;
        Assert.True(model.Blame.IsBusy);
        h.History.Gate.Open();
        await open;

        Assert.False(model.Blame.IsBusy);
        Assert.Contains(nameof(model.Blame.IsBusy), changed);
    }

    // Another file blamed after the first shows none of the first one's lines while it is read.
    [Fact]
    public async Task WhenAnotherFileIsBlamed_ShouldShowNoneOfTheFirstOnesLinesWhileItIsRead()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        Assert.NotEmpty(model.Blame.Rows);
        h.History.Gate.IsClosed = true;

        var open = model.Blame.OpenAsync("b.txt", null);
        await h.History.Gate.Arrival(2);
        Assert.Empty(model.Blame.Rows);
        h.History.Gate.Open();
        await open;

        Assert.Equal(4, model.Blame.Rows.Count);
    }

    // HEAD moved while the file was read: the read goes with the history's other reads, and the blame is read again once
    // the repository has settled, instead of staying empty; a blame that came in is not read again by the next refresh.
    [Fact]
    public async Task WhenTheHistoryMovesWhileTheFileIsRead_ShouldReadItAgain()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        var reads = 0;
        h.History.Blames = (_, _) => { reads++; return Blame(First); };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        h.History.Gate.IsClosed = true;

        var open = model.Blame.OpenAsync("a.txt", Snapshots.Id('a'));
        await h.History.Gate.Started;
        h.Reader.Snapshot = h.Reader.Snapshot with { Head = Snapshots.Id('b') };
        await model.RefreshCommand.ExecuteAsync(null);
        h.History.Gate.Open();
        await open;
        await model.Repository.Blame.Blame.Completion;

        Assert.Equal((1, ""), (model.Blame.Rows.Count, model.Blame.Message));
        reads = 0;
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Repository.Blame.Blame.Completion;
        Assert.Equal(0, reads);
    }

    // A change to another file while the blamed one is read leaves the read alone.
    [Fact]
    public async Task WhenAnotherFileChangesWhileTheFileIsRead_ShouldNotStartTheReadAgain()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        var reads = 0;
        h.History.Blames = (_, _) => { reads++; return Blame(First); };
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        var settled = 0;
        model.Repository.Status.Settled += _ => settled++;
        h.History.Gate.IsClosed = true;

        var open = model.Blame.OpenAsync("a.txt", null);
        await h.History.Gate.Started;
        h.Watcher.Latest.Push("b.txt");
        await ShellHarness.Until(() => settled == 1, "the change to b.txt");
        h.History.Gate.Open();
        await open;
        await model.Repository.Blame.Blame.Completion;

        Assert.Equal((1, 1), (reads, model.Blame.Rows.Count));
    }

    // The shell's Cancel stops the read for good: no later change starts it again, until the file is blamed anew.
    [Fact]
    public async Task WhenTheReadIsCancelled_ShouldNotReadItAgainOnALaterChange()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        var reads = 0;
        h.History.Blames = (_, _) => { reads++; return Blame(First); };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        h.History.Gate.IsClosed = true;

        var open = model.Blame.OpenAsync("a.txt", null);
        await h.History.Gate.Started;
        model.CancelCommand.Execute(null);
        h.History.Gate.Open();
        await open;
        reads = 0;
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Repository.Blame.Blame.Completion;

        Assert.Equal((0, 0), (reads, model.Blame.Rows.Count));
    }

    // A file blamed in the working tree is blamed again when it changes, keeping its lines on screen while it is read,
    // and a Cancel with no blame read in flight does not stop that; another file's change, a change under a file blamed at
    // a commit, or any change once the blame is closed, reads nothing.
    [Fact]
    public async Task WhenTheBlamedWorkingFileChanges_ShouldBlameItAgainWhileTheBlameIsOpen()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false, files: [Snapshots.Modified("a.txt")]);
        var (reads, lines) = (0, 1);
        h.History.Blames = (_, _) => { reads++; return Blame([.. Enumerable.Repeat(Local, lines)]); };
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Blame.OpenAsync("a.txt", null);
        var settled = 0;
        model.Repository.Status.Settled += _ => settled++;
        var emptied = false;
        model.Blame.PropertyChanged += (_, e) => emptied |= e.PropertyName == nameof(model.Blame.Rows) && model.Blame.Rows.Count == 0;

        async Task SettleAsync(string path, int count)
        {
            h.Watcher.Latest.Push(path);
            await ShellHarness.Until(() => settled == count, "the change to " + path);
            await model.Repository.Blame.Blame.Completion;
        }

        await SettleAsync("b.txt", 1);
        Assert.Equal(1, reads);
        lines = 2;
        await SettleAsync("a.txt", 2);
        Assert.Equal((2, 2, false), (reads, model.Blame.Rows.Count, emptied));
        model.Repository.Cancels.CancelAll();
        await SettleAsync("a.txt", 3);
        Assert.Equal(3, reads);
        await model.Blame.OpenAsync("a.txt", Snapshots.Id('a'));
        await SettleAsync("a.txt", 4);
        Assert.Equal(4, reads);
        model.Blame.CloseCommand.Execute(null);
        await model.Blame.OpenAsync("a.txt", null);
        model.Blame.CloseCommand.Execute(null);
        await SettleAsync("a.txt", 5);
        Assert.Equal(5, reads);
    }

    // A binary file has no lines to blame: the view says so instead of showing its bytes.
    [Fact]
    public async Task WhenTheFileIsBinary_ShouldSaySoAndShowNoLines()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        h.History.Blames = (_, _) => new([new BlameLine(1, "PNG", First), new BlameLine(2, "\u0000\u0000IHDR", First)]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        await model.Blame.OpenAsync("logo.png", Snapshots.Id('a'));

        Assert.Equal((BlameText.Binary, 0), (model.Blame.Message, model.Blame.Rows.Count));
    }

    // Whitespace ignored or moved lines detected: the shown file is blamed again with Git told so, its lines kept on screen
    // meanwhile, and so is the next one.
    [Fact]
    public async Task WhenAnOptionIsChosen_ShouldBlameTheFileAgainWithIt()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var changed = new List<string?>();
        var emptied = false;
        model.Blame.PropertyChanged += (_, e) =>
        {
            changed.Add(e.PropertyName);
            emptied |= e.PropertyName == nameof(model.Blame.Rows) && model.Blame.Rows.Count == 0;
        };
        Assert.Equal(BlameOptions.None, h.History.LastBlameOptions);

        model.Blame.IgnoreWhitespace = true;
        await model.Repository.Blame.Blame.Completion;
        Assert.Equal(new BlameOptions(IgnoreWhitespace: true, DetectMoves: false), h.History.LastBlameOptions);
        model.Blame.DetectMoves = true;
        await model.Repository.Blame.Blame.Completion;
        Assert.Equal(new BlameOptions(IgnoreWhitespace: true, DetectMoves: true), h.History.LastBlameOptions);
        Assert.Equal((true, true, false), (changed.Contains(nameof(model.Blame.IgnoreWhitespace)), changed.Contains(nameof(model.Blame.DetectMoves)), emptied));

        await model.Blame.OpenAsync("b.txt", null);
        Assert.Equal((new BlameOptions(true, true), "b.txt", true, true),
            (h.History.LastBlameOptions, h.History.LastBlame?.Path, model.Blame.IgnoreWhitespace, model.Blame.DetectMoves));
    }

    [Fact]
    public async Task WhenTheFileCannotBeBlamed_ShouldSaySo()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        var reads = 0;
        h.History.Blames = (_, _) => { reads++; throw new InvalidOperationException("No such path."); };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var changed = new List<string?>();
        model.Blame.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await model.Blame.OpenAsync("gone.txt", Snapshots.Id('a'));

        Assert.Equal((BlameText.NotRead, 0), (model.Blame.Message, model.Blame.Rows.Count));
        Assert.Contains(nameof(model.Blame.Message), changed);
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Repository.Blame.Blame.Completion;
        Assert.Equal(1, reads);
    }
}
