using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Session;

public sealed class SessionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheSelectionChangesBetweenEditChunks_ShouldKeepTheNewSelectionOverTheCapturedOne(bool staged)
    {
        var dispatcher = new HeldDispatcher();
        var workspace = new FakeRepositoryWorkspace
        {
            Snapshot = Snapshots.Create(files: [Snapshots.PartiallyStaged("a"), Snapshots.PartiallyStaged("b")])
        };
        await using var model = ShellHarness.Compose(workspace, dispatcher: dispatcher);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.FileStatus.UnstagedSelection.Add(model.FileStatus.SelectedUnstaged);
        await model.WorkingDiff.Diff.Completion;
        dispatcher.Hold = true;
        var next = Snapshots.Create(files: [Snapshots.PartiallyStaged("a"), Snapshots.PartiallyStaged("b"), Snapshots.Untracked("c")]);
        var updating = model.Session.UpdateSnapshotAsync(next, CancellationToken.None);
        var action = await dispatcher.Pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The list's own selection follows the choice, as a click makes it.
        model.FileStatus.UnstagedSelection.Clear();
        if (staged) model.FileStatus.StagedSelection.Add(model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[1]);
        else model.FileStatus.UnstagedSelection.Add(model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[1]);
        dispatcher.Hold = false;
        action();
        dispatcher.Completion.SetResult();
        await updating;
        Assert.Equal("b", model.FileStatus.SelectedStatus!.PathText);
        Assert.Equal(staged, model.FileStatus.SelectedStatus.FromIndex);
        Assert.Null(staged ? model.FileStatus.SelectedUnstaged : model.FileStatus.SelectedStaged);
        Assert.Equal(["b"], model.FileStatus.StagedSelection.Concat(model.FileStatus.UnstagedSelection).Select(row => row.PathText));
        Assert.Same(next, model.Repository.Snapshot);
    }

    [Fact]
    public async Task WhenAFileLeavesTheMultiSelectionBetweenEditChunks_ShouldNotSelectItAgain()
    {
        var dispatcher = new HeldDispatcher();
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Modified("b")]) };
        await using var model = ShellHarness.Compose(workspace, dispatcher: dispatcher);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.FileStatus.UnstagedSelection.Add(model.FileStatus.SelectedUnstaged);
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[1]);
        await model.WorkingDiff.Diff.Completion;
        dispatcher.Hold = true;
        var updating = model.Session.UpdateSnapshotAsync(
            Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Modified("b"), Snapshots.Untracked("c")]), CancellationToken.None);
        var action = await dispatcher.Pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A Ctrl+click takes b out of the list's selection and leaves a the selected file.
        model.FileStatus.UnstagedSelection.RemoveAt(1);
        dispatcher.Hold = false;
        action();
        dispatcher.Completion.SetResult();
        await updating;
        Assert.Equal("a", model.FileStatus.SelectedStatus!.PathText);
        Assert.Equal(["a"], model.FileStatus.UnstagedSelection.Select(row => row.PathText));
    }

    [Fact]
    public async Task WhenTheWatcherRestarts_ShouldRejectTheOldWatchersUpdate()
    {
        var workspace = new DelayedLiveWorkspace();
        var model = ShellHarness.Compose(workspace);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            var old = model.Session.Watcher.Completion;
            model.Session.Watcher.Start();
            workspace.Streams[0].Writer.TryWrite(new(Snapshots.Create(branch: "stale")));
            await old.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("main", model.Header.BranchText);
            Assert.Equal("Watching for changes", model.Session.Watcher.Status);

            var current = new TaskCompletionSource();
            model.Repository.SnapshotApplied += (snapshot, _) =>
            {
                if (snapshot.Branch == "current") current.TrySetResult();
            };
            workspace.Streams[1].Writer.TryWrite(new(Snapshots.Create(branch: "current")));
            await current.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("current", model.Header.BranchText);
        }
        finally
        {
            workspace.CompleteWatches();
            await model.DisposeAsync();
        }
    }

    [Fact]
    public async Task WhenTheRepositoryClosesAndTheShellIsDisposed_ShouldWaitForTheCanceledWatcherAndKeepTheScreenClear()
    {
        var workspace = new DelayedLiveWorkspace();
        var model = ShellHarness.Compose(workspace);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        var disposing = model.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        Assert.False(workspace.Disposed);
        workspace.Streams[0].Writer.TryWrite(new(null));
        workspace.CompleteWatches();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workspace.Disposed);
        Assert.False(model.Repository.HasRepository);
        Assert.Equal("No repository open", model.Header.RepositoryName);
        Assert.Empty(model.Session.Watcher.Status);
        Assert.Empty(model.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenQueuedFileEditsAreCanceled_ShouldNeitherChangeTheListsNorPublishTheSnapshot()
    {
        var dispatcher = new HeldDispatcher();
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]) };
        await using var model = ShellHarness.Compose(workspace, dispatcher: dispatcher);
        await model.Session.OpenRepositoryAsync("fixture");
        var snapshot = model.Repository.Snapshot;
        using var request = new CancellationTokenSource();
        dispatcher.Hold = true;
        var update = model.Session.UpdateSnapshotAsync(Snapshots.Create(files: [Snapshots.Modified("b")]), request.Token);
        var action = await dispatcher.Pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        request.Cancel();
        action();
        dispatcher.Completion.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.Same(snapshot, model.Repository.Snapshot);
        Assert.Equal("a", ((Trag.ViewModels.Status.StatusRow)model.FileStatus.UnstagedFiles[0]).PathText);
    }

    [Fact]
    public async Task WhenASnapshotIsAppliedOnOpenOrRefresh_ShouldShowItsSubscribersTheMatchingFileLists()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]) };
        await using var model = ShellHarness.Compose(workspace);
        var applied = 0;
        // Every open starts a repository of its own; the check follows each one.
        model.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RepositorySession.Current)) return;
            model.Session.Current.SnapshotApplied += (snapshot, _) =>
            {
                applied++;
                Assert.Equal(snapshot.Files.Count(file => file.IsStaged), model.FileStatus.StagedFiles.Count);
                Assert.Equal(snapshot.Files.Count(file => file.IsUnstaged), model.FileStatus.UnstagedFiles.Count);
            };
        };
        await model.Session.OpenRepositoryAsync("fixture");
        workspace.Snapshot = Snapshots.Create(files: [Snapshots.PartiallyStaged("b"), Snapshots.Untracked("c")]);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, applied);
    }

    private sealed class HeldDispatcher : IViewDispatcher
    {
        public bool Hold { get; set; }
        public TaskCompletionSource<Action> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InvokeAsync(Action action)
        {
            if (!Hold) { action(); return Task.CompletedTask; }
            Pending.TrySetResult(action);
            return Completion.Task;
        }
    }

    private sealed class DelayedLiveWorkspace : IRepositoryWorkspace, ILiveRepositoryWorkspace
    {
        public List<Channel<RepositoryUpdate>> Streams { get; } = [];
        public bool Disposed { get; private set; }
        public Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken ct) => Task.FromResult<RepositorySnapshot?>(Snapshots.Create() with { Root = path });
        public Task<RepositorySnapshot?> RefreshAsync(CancellationToken ct) => Task.FromResult<RepositorySnapshot?>(Snapshots.Create());
        public Task CloseAsync() => Task.CompletedTask;
        public Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken ct) => Task.FromResult(GitWriteResult.Rejected("Nothing is written here."));
        public bool IsCurrent(RepositorySnapshot snapshot) => true;
        public Task<FileDiff?> ReadDiffAsync(FileStatus file, bool fromIndex, bool preview, CancellationToken ct) => Task.FromResult<FileDiff?>(null);
        public Task<ConflictText?> ReadConflictAsync(FileStatus file, CancellationToken ct) => Task.FromResult<ConflictText?>(null);
        public async IAsyncEnumerable<RepositoryUpdate> WatchAsync([EnumeratorCancellation] CancellationToken ct)
        {
            var stream = Channel.CreateUnbounded<RepositoryUpdate>();
            Streams.Add(stream);
            // Simulate an external source that can still deliver after cancellation.
            await foreach (var update in stream.Reader.ReadAllAsync()) yield return update;
        }
        public void CompleteWatches() { foreach (var stream in Streams) stream.Writer.TryComplete(); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
