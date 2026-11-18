using Trag.Application.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Diff;

// The bridge between the presentation builder and the diff panes: late results are rejected, closing releases the
// presentation, layout preferences round-trip. The builder's own projections are tested in Trag.Domain.UnitTests.
public sealed class DiffPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenASelectionOrRefreshOvertakesAPendingHighlight_ShouldDropTheLateOneAndEmptyThePaneOnClose(bool refresh)
    {
        var h = Harness(); var reader = h.Reader; var builder = new DelayedBuilder();
        await using var model = h.Shell(diffBuilder: builder);
        await model.Session.OpenRepositoryAsync("fixture"); model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        var previous = model.WorkingDiff.Diff.Completion; await builder.Started.Task;
        if (refresh) { reader.Snapshot = reader.Snapshot with { IndexFingerprint = "current" }; await model.RefreshCommand.ExecuteAsync(null); }
        else model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        await model.WorkingDiff.Diff.Completion;
        Assert.True(builder.Token.IsCancellationRequested);
        builder.Complete.SetResult(await Build([new('+', "+stale")])); await previous;
        Assert.Equal("+current", Assert.Single(model.WorkingDiff.Diff.Document.Presentation.Lines).Text);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Same(DiffPresentation.Empty, model.WorkingDiff.Diff.Document.Presentation);
    }
    private static Task<DiffPresentation> Build(PatchLine[] lines) => new BackgroundDiffBuilder().BuildAsync(new([], lines, DiffContentKind.Text, ""), "sample.cs", default);

    [Fact]
    public async Task WhenAnotherCommitFileOvertakesAPendingHighlight_ShouldDropTheLateOneAndEmptyThePaneOnClose()
    {
        var builder = new DelayedBuilder();
        await using var model = Harness().Shell(diffBuilder: builder);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.History.Details.Panel.SelectedFile = model.History.Details.Panel.Files[0]; var previous = model.History.Details.Diff.Completion;
        await builder.Started.Task;
        model.History.Details.Panel.SelectedFile = model.History.Details.Panel.Files[1]; await model.History.Details.Diff.Completion;
        Assert.True(builder.Token.IsCancellationRequested);
        builder.Complete.SetResult(await Build([new('+', "+stale")])); await previous;
        Assert.Equal("+second.cs", Assert.Single(model.History.Details.Diff.Document.Presentation.Lines).Text);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Same(DiffPresentation.Empty, model.History.Details.Diff.Document.Presentation);
    }
    private sealed class DelayedBuilder : IDiffPresentationBuilder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DiffPresentation> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        private int calls;
        public Task<DiffPresentation> BuildAsync(FileDiff diff, string path, CancellationToken ct)
        { if (++calls != 1) return new BackgroundDiffBuilder().BuildAsync(diff, path, ct); Token = ct; Started.SetResult(); return Complete.Task; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WhenTheRepositoryClosesOrAWriteStartsDuringAHighlight_ShouldDropThePendingPresentation(bool history, bool write)
    {
        var h = Harness();
        var builder = new DelayedBuilder();
        var writer = h.Writer;
        await using var model = h.Shell(diffBuilder: builder);
        await model.Session.OpenRepositoryAsync("fixture");
        if (history)
        {
            model.Navigator.ShowHistoryCommand.Execute(null);
            await model.History.HistoryCompletion;
            model.History.SelectedCommit = model.History.Page.Commits[0];
            await model.History.Details.Completion;
            model.History.Details.Panel.SelectedFile = model.History.Details.Panel.Files[0];
        }
        else model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        var diff = history ? model.History.Details.Diff : model.WorkingDiff.Diff;
        var pending = diff.Completion;
        await builder.Started.Task;

        var mutation = Task.CompletedTask;
        if (write)
        {
            writer.Gate.IsClosed = true;
            mutation = model.Repository.Writer.RunAsync(snapshot => new Trag.Domain.Writes.StageRequest(snapshot, [Snapshots.Path("sample.cs")]));
            await writer.Gate.Started;
        }
        else await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.True(builder.Token.IsCancellationRequested);
        builder.Complete.SetResult(await Build([new('+', "+stale")]));
        await pending;
        Assert.Empty(diff.Document.Presentation.Lines);
        Assert.False(diff.IsBusy);
        if (write)
        {
            writer.Gate.Open();
            await mutation;
        }
    }
    // What the layout keeps and writes is LayoutPreferencesTests'.
    [Fact]
    public async Task WhenTheShellIsComposedWithAPreferencesStore_ShouldTakeItsLayoutFromIt()
    {
        FakeLayoutStore store = new() { Preferences = new(1, 0, 1, 2, 0, 1, 0.7) };
        await using var model = ShellHarness.Compose(layout: store);
        await model.Layout.LoadLayoutAsync();
        Assert.Equal(2, model.Layout.ThemeIndex); Assert.Equal(0, model.Layout.HistoryPanelRatio); Assert.Equal(1, model.Layout.StagePanelRatio); Assert.Equal(0.7, model.Layout.CommitPanelRatio);
    }
    // One file modified in both the index and the working tree, whose index diff names the fingerprint of the read that
    // produced the snapshot, and one commit with two files in it, so a second selection can overtake the first.
    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(head: "head", files: [Snapshots.PartiallyStaged("sample.cs")], identity: false, indexFingerprint: "old");
        h.Diff.Diff = (_, index, _) => new FileDiff([], [new('+', "+" + (index ? h.Reader.Snapshot.IndexFingerprint : "current"))], DiffContentKind.Text, "");
        h.History.Pages = (_, _) => new([Snapshots.Commit("head", "Message")], false, 0);
        h.History.Details = _ => new(Snapshots.Commit("head", "Message"), "Message",
            [new(Snapshots.Path("first.cs"), "A", "", "", "", ""), new(Snapshots.Path("second.cs"), "A", "", "", "", "")]);
        return h;
    }
}
