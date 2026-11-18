using Trag.Domain.Diff;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Session;

public sealed class LiveViewModelTests
{
    // The close clears the selection the second time.
    [Fact]
    public async Task WhenTheSelectionIsClearedAgainAfterItsCanceledDiffEnded_ShouldNotCancelTheDisposedRequest()
    {
        var h = Harness(stale: true); var diff = h.Diff; diff.Gate.IsClosed = true;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        var pending = model.WorkingDiff.Diff.Completion;
        await diff.Gate.Started;
        model.FileStatus.SelectedStaged = null;
        diff.Gate.Open();
        await pending; // The canceled request has now disposed its token source.
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasRepository);
        Assert.Empty(model.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenTheSelectionSwitchesWhileADiffReads_ShouldCancelTheOldDiffAndNeverShowItsLateResult()
    {
        var h = Harness(stale: true); var diff = h.Diff; diff.Gate.IsClosed = true;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        var old = model.WorkingDiff.Diff.Completion;
        await diff.Gate.Started;
        diff.Gate.IsClosed = false; // the next read answers at once, so the held one has to be overtaken
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        Assert.True(diff.Gate.FirstToken.IsCancellationRequested);
        Assert.Equal("current", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
        diff.Gate.Open();
        await old;
        Assert.Equal("current", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Empty(model.WorkingDiff.Diff.Document.Lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenARefreshKeepsTheSelectedFilesStatus_ShouldRereadItsContentOnEitherSide(bool fromIndex)
    {
        var content = "before";
        var h = Harness(); var diff = h.Diff;
        diff.Diff = (_, _, _) => new([], [new('+', content)], DiffContentKind.Text, "");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        if (fromIndex)
            model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        else
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal("before", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
        var selected = model.FileStatus.SelectedStatus;

        // The snapshot still says MM; the content behind that status has changed.
        content = "after";
        diff.Gate.IsClosed = true;
        await model.RefreshCommand.ExecuteAsync(null);
        try
        {
            await diff.Gate.Arrival(2).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(model.WorkingDiff.Diff.Completion.IsCompleted);
            Assert.Equal(selected!.File, model.FileStatus.SelectedStatus!.File);
            Assert.Equal(fromIndex, model.FileStatus.SelectedStatus.FromIndex);
        }
        finally
        {
            diff.Gate.Open();
        }
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal(2, diff.Requests.Count);
        Assert.Equal(fromIndex, diff.Requests[^1].FromIndex);
        Assert.Equal("after", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
    }

    // The preview is offered only while its diff is shown; a section change drops the diff here.
    [Fact]
    public async Task WhenThePreviewIsLoaded_ShouldReadTheShownFileAgainPastTheLimitAndGoAwayWithTheDiff()
    {
        var h = Harness(); var diff = h.Diff;
        diff.Diff = (_, _, preview) => new FileDiff([], [new('+', preview ? "whole" : "cut")], DiffContentKind.Text, "", CanPreview: !preview);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var row = (StatusRow)model.FileStatus.StagedFiles[0];
        model.FileStatus.SelectedStaged = row;
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal("cut", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
        Assert.True(model.WorkingDiff.Diff.LoadPreviewCommand.CanExecute(null));

        model.WorkingDiff.Diff.LoadPreviewCommand.Execute(null);
        await model.WorkingDiff.Diff.Completion;

        Assert.Equal((row.File, true, true), diff.Requests[^1]);
        Assert.Equal("whole", Assert.Single(model.WorkingDiff.Diff.Document.Lines).Text);
        Assert.False(model.WorkingDiff.Diff.LoadPreviewCommand.CanExecute(null));

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        Assert.True(model.WorkingDiff.Diff.LoadPreviewCommand.CanExecute(null));
        model.Navigator.ShowHistory();
        Assert.False(model.WorkingDiff.Diff.LoadPreviewCommand.CanExecute(null));
        Assert.Empty(model.WorkingDiff.Diff.Document.Lines);
        Assert.Equal(3, diff.Requests.Count);
    }
    // One file changed in both the index and the working tree. With stale, the staged side answers "stale": it is the
    // side a test holds, so it is the one whose late result has to be discarded.
    private static ShellHarness Harness(bool stale = false)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "abc", identity: false, files: [Snapshots.PartiallyStaged("a")]);
        if (stale) h.Diff.Diff = (_, index, _) => new FileDiff([], [new('+', index ? "stale" : "current")], DiffContentKind.Text, "");
        return h;
    }
}
