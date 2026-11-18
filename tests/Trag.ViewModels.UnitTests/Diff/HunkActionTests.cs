using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Diff;

// The hunk actions of the shown working diff: offered for a file changed on the shown side whose diff is read whole as
// text, they stage or unstage one hunk of the shown lines, or discard one of the working tree's after a question, and
// the diff stays on screen while they do, so its place is kept when the rest of the file is read again.
public sealed class HunkActionTests
{
    // a.txt: d to D and f to F, two hunks.
    private static readonly PatchLine[] TwoChanges =
    [
        new(' ', " a", 1, 1), new(' ', " b", 2, 2), new(' ', " c", 3, 3), new('-', "-d", 4), new('+', "+D", null, 4), new(' ', " e", 5, 5),
        new('-', "-f", 6), new('+', "+F", null, 6), new(' ', " g", 7, 7)
    ];

    private static FileDiff Diff(IReadOnlyList<PatchLine> lines, DiffContentKind kind = DiffContentKind.Text) => new([], lines, kind, "");

    // Lines chosen in the diff shown now, as the view chooses them.
    private static HunkChoice Shown(ShellViewModel model, IReadOnlyList<int> lines) => new(model.WorkingDiff.Diff.Document.Lines, lines);

    private static async Task<(ShellHarness Harness, ShellViewModel Model)> ShowAsync(FileStatus file, bool fromIndex, DiffContentKind kind = DiffContentKind.Text)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [file]);
        h.Diff.Diff = (_, _, _) => Diff(TwoChanges, kind);
        var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        if (fromIndex) model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        else model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        return (h, model);
    }

    [Fact]
    public async Task WhenTheWorkingTreesDiffOfAModifiedFileIsShown_ShouldOfferToStageAHunk()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        h.Diff.Diff = (_, _, _) => Diff(TwoChanges);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        List<string> offered = [];
        List<bool> allowed = [];
        model.WorkingDiff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkingDiffViewModel.Offer)) offered.Add(model.WorkingDiff.Offer.Hunk); };
        model.WorkingDiff.TakeHunkCommand.CanExecuteChanged += (_, _) => allowed.Add(model.WorkingDiff.TakeHunkCommand.CanExecute(null));

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;

        Assert.Equal(StatusText.HunkAction(fromIndex: false), model.WorkingDiff.Offer.Hunk);
        Assert.Equal(StatusText.HunkAction(fromIndex: false), offered[^1]);
        Assert.False(model.WorkingDiff.Offer.Unstages);
        Assert.True(allowed[^1]);
    }

    // The view shows a minus for what goes back out of the index.
    [Fact]
    public async Task WhenTheIndexsDiffIsShown_ShouldSayItsActionsUnstage()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.PartiallyStaged("a.txt")]);
        h.Diff.Diff = (_, _, _) => Diff(TwoChanges);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        List<bool> unstages = [];
        model.WorkingDiff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkingDiffViewModel.Offer)) unstages.Add(model.WorkingDiff.Offer.Unstages); };

        model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        await model.WorkingDiff.Diff.Completion;

        Assert.True(model.WorkingDiff.Offer.Unstages);
        Assert.True(unstages[^1]);
    }

    // A limited diff shows only part of the file, so its hunks are not the file's.
    [Fact]
    public async Task WhenTheDiffIsNotReadWholeAsText_ShouldOfferNoHunk()
    {
        var (_, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false, DiffContentKind.Limited);
        await using var _ = model;

        Assert.Equal("", model.WorkingDiff.Offer.Hunk);
    }

    [Fact]
    public async Task WhenAHunkIsTaken_ShouldStageThatHunkOfTheShownFile()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;

        await model.WorkingDiff.TakeHunkCommand.ExecuteAsync(Shown(model, new DiffBlock(6, 7).Lines));

        var request = Assert.IsType<PatchRequest>(h.Writer.LastRequest);
        Assert.Equal((PatchAction.Stage, "a.txt", HunkPatch.For(TwoChanges, new DiffBlock(6, 7).Lines, Snapshots.Path("a.txt"), PatchAction.Stage)),
            (request.Action, request.Path.Display, request.Patch));
    }

    // The file stays selected, and the rest of it is read again in place of the shown diff, never through an empty or a
    // loading document.
    [Fact]
    public async Task WhenAHunkIsTaken_ShouldKeepTheFileSelectedAndItsDiffOnScreenUntilTheRestReplacesIt()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        PatchLine[] rest = [.. TwoChanges[..3], new(' ', " d", 4, 4), new(' ', " e", 5, 5), .. TwoChanges[6..]];
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.PartiallyStaged("a.txt")]);
            h.Diff.Diff = (_, _, _) => Diff(rest);
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Done"));
        };
        List<DiffDocument> shown = [];
        model.WorkingDiff.Diff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DiffViewModel.Document)) shown.Add(model.WorkingDiff.Diff.Document); };

        await model.WorkingDiff.TakeHunkCommand.ExecuteAsync(Shown(model, new DiffBlock(3, 4).Lines));
        await ShellHarness.Until(() => model.WorkingDiff.Diff.Document.Lines.Count == rest.Length, "the rest of the file");

        Assert.All(shown, document => Assert.Equal(rest.Length, document.Lines.Count));
        Assert.Equal("a.txt", model.FileStatus.SelectedUnstaged?.PathText);
    }

    // A refresh holds every write: the shown diff stays, and its hunks wait until the refresh ends.
    [Fact]
    public async Task WhenTheRepositoryIsBeingRead_ShouldWaitBeforeTakingAHunk()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        List<bool> allowed = [];
        model.WorkingDiff.TakeHunkCommand.CanExecuteChanged += (_, _) => allowed.Add(model.WorkingDiff.TakeHunkCommand.CanExecute(null));
        var reads = h.Reader.Gate.Entries;
        h.Reader.Gate.IsClosed = true;

        var refreshing = model.RefreshCommand.ExecuteAsync(null);
        await h.Reader.Gate.Arrival(reads + 1);

        Assert.False(allowed[^1]);
        h.Reader.Gate.Open();
        await refreshing;
        Assert.True(allowed[^1]);
    }

    [Fact]
    public async Task WhenTheWorkingTreesDiffOfAModifiedFileIsShown_ShouldOfferToDiscardAHunk()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        h.Diff.Diff = (_, _, _) => Diff(TwoChanges);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        List<string> offered = [];
        List<bool> allowed = [];
        model.WorkingDiff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkingDiffViewModel.Offer)) offered.Add(model.WorkingDiff.Offer.DiscardHunk); };
        model.WorkingDiff.DiscardHunkCommand.CanExecuteChanged += (_, _) => allowed.Add(model.WorkingDiff.DiscardHunkCommand.CanExecute(null));

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;

        Assert.Equal(StatusText.DiscardHunk, model.WorkingDiff.Offer.DiscardHunk);
        Assert.Equal(StatusText.DiscardHunk, offered[^1]);
        Assert.True(allowed[^1]);
    }

    [Fact]
    public async Task WhenChosenLinesAreTaken_ShouldStageThoseLinesAlone()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;

        await model.WorkingDiff.TakeHunkCommand.ExecuteAsync(Shown(model, [4, 7]));

        Assert.Equal(HunkPatch.For(TwoChanges, [4, 7], Snapshots.Path("a.txt"), PatchAction.Stage), Assert.IsType<PatchRequest>(h.Writer.LastRequest).Patch);
    }

    // What is staged is unstaged, not discarded, even when the file has working tree changes too: those are another diff.
    [Fact]
    public async Task WhenTheIndexsDiffIsShown_ShouldOfferNoDiscard()
    {
        var (_, model) = await ShowAsync(Snapshots.PartiallyStaged("a.txt"), fromIndex: true);
        await using var _ = model;

        Assert.Equal("", model.WorkingDiff.Offer.DiscardHunk);
        Assert.False(model.WorkingDiff.DiscardHunkCommand.CanExecute(null));
    }

    // The lines are chosen in the diff the view shows. The file's diff read again before the click moves its lines, and
    // what is staged is still what was chosen, never the lines now at the same places.
    [Fact]
    public async Task WhenTheShownDiffIsReadAgainAfterTheLinesWereChosen_ShouldStageTheLinesOfTheDiffTheyWereChosenIn()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        var chosen = new HunkChoice(model.WorkingDiff.Diff.Document.Lines, new DiffBlock(6, 7).Lines);
        await ReadAgainWithMovedLinesAsync(h, model);

        await model.WorkingDiff.TakeHunkCommand.ExecuteAsync(chosen);

        Assert.Equal(HunkPatch.For(TwoChanges, new DiffBlock(6, 7).Lines, Snapshots.Path("a.txt"), PatchAction.Stage),
            Assert.IsType<PatchRequest>(h.Writer.LastRequest).Patch);
    }

    [Fact]
    public async Task WhenTheShownDiffIsReadAgainAfterTheLinesWereChosen_ShouldDiscardTheLinesOfTheDiffTheyWereChosenIn()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        var chosen = new HunkChoice(model.WorkingDiff.Diff.Document.Lines, new DiffBlock(6, 7).Lines);
        await ReadAgainWithMovedLinesAsync(h, model);

        model.WorkingDiff.DiscardHunkCommand.Execute(chosen);
        await model.WorkingDiff.HunkDiscard.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(HunkPatch.For(TwoChanges, new DiffBlock(6, 7).Lines, Snapshots.Path("a.txt"), PatchAction.Discard),
            Assert.IsType<PatchRequest>(h.Writer.LastRequest).Patch);
    }

    // Another program added a line at the top of a.txt: every change of the shown diff is one line further down.
    private static async Task ReadAgainWithMovedLinesAsync(ShellHarness h, ShellViewModel model)
    {
        PatchLine[] moved = [new('+', "+top", null, 1), .. TwoChanges.Select(line => line with { NewNumber = line.NewNumber + 1 })];
        h.Diff.Diff = (_, _, _) => Diff(moved);
        h.Watcher.Latest.Push("a.txt");
        await ShellHarness.Until(() => model.WorkingDiff.Diff.Document.Lines.Count == moved.Length, "the diff with the moved lines");
    }

    // There is no undo, so the button only asks.
    [Fact]
    public async Task WhenAHunkIsToBeDiscarded_ShouldAskAboutThatHunkFirstAndWriteNothing()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;

        model.WorkingDiff.DiscardHunkCommand.Execute(Shown(model, new DiffBlock(6, 7).Lines));

        var question = model.WorkingDiff.HunkDiscard;
        Assert.True(question.IsOpen);
        Assert.Equal(("Discard this hunk?", "a.txt"), (question.Subject!.Title, question.Text));
        Assert.Equal(new DiffBlock(6, 7).Lines, question.Subject.Chosen);
        Assert.Empty(h.Writer.Requests);
    }

    [Fact]
    public async Task WhenTheDiscardIsConfirmed_ShouldDiscardThatHunkAndKeepTheFileAndItsDiffOnScreen()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        PatchLine[] rest = [.. TwoChanges[..6], new(' ', " f", 6, 6), new(' ', " g", 7, 7)];
        h.Writer.Handle = (_, _) =>
        {
            h.Diff.Diff = (_, _, _) => Diff(rest);
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Done"));
        };
        List<DiffDocument> shown = [];
        model.WorkingDiff.Diff.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DiffViewModel.Document)) shown.Add(model.WorkingDiff.Diff.Document); };
        model.WorkingDiff.DiscardHunkCommand.Execute(Shown(model, new DiffBlock(6, 7).Lines));

        await model.WorkingDiff.HunkDiscard.ConfirmCommand.ExecuteAsync(null);
        await ShellHarness.Until(() => model.WorkingDiff.Diff.Document.Lines.Count == rest.Length, "the rest of the file");

        var request = Assert.IsType<PatchRequest>(h.Writer.LastRequest);
        Assert.Equal((PatchAction.Discard, HunkPatch.For(TwoChanges, new DiffBlock(6, 7).Lines, Snapshots.Path("a.txt"), PatchAction.Discard)), (request.Action, request.Patch));
        Assert.False(model.WorkingDiff.HunkDiscard.IsOpen);
        Assert.All(shown, document => Assert.Equal(rest.Length, document.Lines.Count));
        Assert.Equal("a.txt", model.FileStatus.SelectedUnstaged?.PathText);
    }

    // A refresh holds every write, the question too.
    [Fact]
    public async Task WhenTheRepositoryIsBeingRead_ShouldWaitBeforeAskingToDiscardAHunk()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        List<bool> allowed = [];
        model.WorkingDiff.DiscardHunkCommand.CanExecuteChanged += (_, _) => allowed.Add(model.WorkingDiff.DiscardHunkCommand.CanExecute(null));
        var reads = h.Reader.Gate.Entries;
        h.Reader.Gate.IsClosed = true;

        var refreshing = model.RefreshCommand.ExecuteAsync(null);
        await h.Reader.Gate.Arrival(reads + 1);

        Assert.False(allowed[^1]);
        h.Reader.Gate.Open();
        await refreshing;
        Assert.True(allowed[^1]);
    }

    [Fact]
    public async Task WhenAHunkIsBeingTaken_ShouldNotTakeAnotherMeanwhile()
    {
        var (h, model) = await ShowAsync(Snapshots.Modified("a.txt"), fromIndex: false);
        await using var _ = model;
        var release = new TaskCompletionSource();
        h.Writer.Handle = async (_, _) => { await release.Task; return new(GitWriteOutcome.Succeeded, "Done"); };

        var taking = model.WorkingDiff.TakeHunkCommand.ExecuteAsync(Shown(model, new DiffBlock(3, 4).Lines));

        Assert.False(model.WorkingDiff.TakeHunkCommand.CanExecute(null));
        release.SetResult();
        await taking;
    }
}
