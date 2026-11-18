using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Diff;

// The shown working diff while files change under it (another program editing the repository): a change to its own
// file, or a read that may have changed anything, reads it again without the loading message or progress, keeps the
// shown diff when nothing in it changed, and replaces it in place when something did.
public sealed class DiffRefreshTests
{
    private static FileDiff Text(params string[] lines) =>
        new([], lines.Select((line, index) => new PatchLine('+', "+" + line, null, index + 1)).ToArray(), DiffContentKind.Text, "");

    private static async Task<(ShellHarness Harness, ShellViewModel Model, List<DiffDocument> Shown, List<bool> Busy)> OpenAsync()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        h.Diff.Diff = (_, _, _) => Text("one");
        var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        List<DiffDocument> shown = [];
        List<bool> busy = [];
        model.WorkingDiff.Diff.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffViewModel.Document)) shown.Add(model.WorkingDiff.Diff.Document);
            else if (e.PropertyName == nameof(DiffViewModel.IsBusy) && model.WorkingDiff.Diff.IsBusy) busy.Add(true);
        };
        return (h, model, shown, busy);
    }

    [Fact]
    public async Task WhenTheShownFileChangesButItsDiffDoesNot_ShouldKeepTheShownDiff()
    {
        var (h, model, shown, busy) = await OpenAsync();
        await using var _ = model;
        var before = model.WorkingDiff.Diff.Document;
        h.Watcher.Latest.Push("a.txt");
        await ShellHarness.Until(() => h.Diff.Requests.Count == 2, "the shown file read again");
        await model.WorkingDiff.Diff.Completion;
        Assert.Same(before, model.WorkingDiff.Diff.Document);
        Assert.Empty(shown); Assert.Empty(busy);
    }

    [Fact]
    public async Task WhenTheShownFilesDiffChanges_ShouldReplaceItWithoutTheLoadingMessage()
    {
        var (h, model, shown, busy) = await OpenAsync();
        await using var _ = model;
        h.Diff.Diff = (_, _, _) => Text("one", "two");
        h.Watcher.Latest.Push("a.txt");
        await ShellHarness.Until(() => shown.Count == 1, "the new diff");
        Assert.Equal(["+one", "+two"], model.WorkingDiff.Diff.Document.Lines.Select(line => line.Text));
        Assert.DoesNotContain(shown, document => document.Message == DiffMessages.Working.Loading);
        Assert.Empty(busy);
    }

    // The new status replaces the row, and the update brings the selection back on the new row; that choice is the
    // update's, so the diff is read again as quietly as for any other change to the file.
    [Fact]
    public async Task WhenTheShownFileGetsAnotherStatus_ShouldReadItsDiffAgainQuietlyOnTheNewRow()
    {
        var (h, model, shown, busy) = await OpenAsync();
        await using var _ = model;
        h.Diff.Diff = (_, _, _) => Text("one", "two");
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("a.txt"), '.', 'D'), Snapshots.Modified("b.txt")]);
        h.Watcher.Latest.Push("a.txt");
        await ShellHarness.Until(() => model.WorkingDiff.Diff.Document.Lines.Count == 2, "the new diff");
        Assert.Equal('D', model.FileStatus.SelectedUnstaged!.File.Worktree);
        Assert.DoesNotContain(shown, document => document.Message == DiffMessages.Working.Loading);
        Assert.Empty(busy);
    }

    [Fact]
    public async Task WhenAReadMayHaveChangedAnything_ShouldReadTheShownFileAgainQuietly()
    {
        var (h, model, shown, busy) = await OpenAsync();
        await using var _ = model;
        h.Watcher.Latest.PushReconcile();
        await ShellHarness.Until(() => h.Diff.Requests.Count == 2, "the shown file read again");
        await model.WorkingDiff.Diff.Completion;
        Assert.Empty(shown); Assert.Empty(busy);
    }

    // Another file changes while the shown file's diff is read, a file the user chose or the shown one read again quietly
    // after its own change, and the update names only the other file: a read that ends before the update is published
    // stands, and one that ends after it is dropped as a read the repository moved under and read again.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WhenAnotherFileChangesWhileTheShownFilesDiffIsRead_ShouldStillShowItsCurrentDiff(bool quietly, bool readEndsFirst)
    {
        var (h, model, _, _) = await OpenAsync();
        await using var _ = model;
        h.Diff.Diff = (_, _, _) => Text("one", "two");
        h.Diff.Gate.IsClosed = true;
        if (quietly) h.Watcher.Latest.Push("a.txt");
        else model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[1];
        await h.Diff.Gate.Arrival(2);
        var other = quietly ? "b.txt" : "a.txt";
        h.Reader.Snapshot = Snapshots.Create(files: [.. new[] { "a.txt", "b.txt" }.Select(path =>
            path == other ? new FileStatus(Snapshots.Path(path), '.', 'D') : Snapshots.Modified(path))]);
        h.Reader.Gate.IsClosed = readEndsFirst;
        var update = h.Reader.Gate.Arrival(h.Reader.Gate.Entries + 1);

        h.Watcher.Latest.Push(other);
        await update;
        if (readEndsFirst)
        {
            h.Diff.Gate.Open();
            await model.WorkingDiff.Diff.Completion;
            h.Reader.Gate.Open();
        }
        await ShellHarness.Until(() => model.FileStatus.UnstagedFiles.Cast<StatusRow>().Any(row => row.File.Worktree == 'D'), "the update");
        h.Diff.Gate.Open();

        await ShellHarness.Until(() => model.WorkingDiff.Diff.Document.Lines.Count == 2, "the shown file's current diff");
        Assert.Equal(quietly ? "a.txt" : "b.txt", model.WorkingDiff.Diff.Document.Presentation.Source!.Path);
    }

    [Fact]
    public async Task WhenTheUserChoosesAnotherFile_ShouldStillShowTheLoadingMessage()
    {
        var (_, model, shown, _) = await OpenAsync();
        await using var __ = model;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[1];
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal(DiffMessages.Working.Loading, shown[0].Message);
        Assert.Equal("b.txt", shown[^1].Presentation.Source!.Path);
    }
}
