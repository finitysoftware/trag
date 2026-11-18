using System.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Session;

public sealed class RepositoryViewModelTests
{
    [Fact]
    public async Task WhenARepositoryIsOpenedAndClosed_ShouldReadOnlyOnOpenShowAPartlyStagedFileOnBothSidesAndClearOnClose()
    {
        var h = Harness(); var reader = h.Reader; var store = h.Store;
        await using var model = h.Shell();
        Assert.Equal(0, reader.Calls);
        Assert.False(model.RefreshCommand.CanExecute(null));
        await model.Session.OpenRepositoryAsync("repository");
        Assert.True(model.Repository.HasRepository);
        Assert.Single(model.FileStatus.StagedFiles);
        Assert.Equal(2, model.FileStatus.UnstagedFiles.Count);
        Assert.Equal("repository", store.Path);
        model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0];
        Assert.Equal("Staged · index", model.FileStatus.SelectedStatus!.Source);
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        Assert.Null(model.FileStatus.SelectedStaged);
        Assert.Equal("Unstaged · working tree", model.FileStatus.SelectedStatus!.Source);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasRepository);
        Assert.Empty(model.FileStatus.StagedFiles);
        Assert.Empty(model.FileStatus.UnstagedFiles);
        Assert.Null(model.FileStatus.SelectedStatus);
    }

    [Fact]
    public async Task WhenTheUserRefreshes_ShouldReadTheRepositoryAgainAndKeepTheSelectedPath()
    {
        var h = Harness(); var reader = h.Reader;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("repository");
        model.FileStatus.SelectedUnstaged = model.FileStatus.UnstagedFiles.Cast<StatusRow>().Single(row => row.PathText == "partial.txt");
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, reader.Calls);
        Assert.Equal("partial.txt", model.FileStatus.SelectedStatus!.PathText);
        Assert.False(model.Repository.IsBusy);
    }

    [Fact]
    public async Task WhenAnotherRepositoryOpensDuringARead_ShouldNotLetTheLateReadRestoreTheOldRepository()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<RepositorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = Harness();
        h.Reader.Read = (path, _) => { if (path != "old") return Task.FromResult(Snapshot(path)); started.TrySetResult(); return delayed.Task; };
        await using var model = h.Shell();
        var old = model.Session.OpenRepositoryAsync("old");
        await started.Task;
        await model.Session.OpenRepositoryAsync("new");
        delayed.SetResult(Snapshot("old"));
        await old;
        Assert.Equal("new", model.Repository.Snapshot!.Root);
        Assert.False(model.Repository.IsBusy);
        Assert.False(model.StatusBar.HasError);
    }

    [Fact]
    public async Task WhenAnOpenIsCanceled_ShouldNotClaimSuccessAndLetRefreshRetryIt()
    {
        var h = Harness(); var reader = h.Reader;
        // The read ends only through the token: cancelling it is what the test does.
        reader.Read = async (_, token) => { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); };
        await using var model = h.Shell();
        var pending = model.Session.OpenRepositoryAsync("repository");
        Assert.True(model.Repository.IsBusy);
        model.CancelCommand.Execute(null);
        await pending;
        Assert.False(model.Repository.HasRepository);
        Assert.Contains("canceled", model.StatusBar.Status);
        Assert.True(model.RefreshCommand.CanExecute(null));
        reader.Read = (path, _) => Task.FromResult(Snapshot(path));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Repository.HasRepository);
    }

    [Fact]
    public async Task WhenTheUserOpensARepositoryDuringASlowStartupRestore_ShouldKeepTheChosenOne()
    {
        var savedPath = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = Harness(); var reader = h.Reader;
        h.Store.Load = _ => savedPath.Task;
        await using var model = h.Shell();
        var initialization = model.InitializeAsync();
        await model.Session.OpenRepositoryAsync("chosen");
        savedPath.SetResult("saved-old");
        await initialization;
        Assert.Equal("chosen", model.Repository.Snapshot!.Root);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task WhenTheLastRepositoryIsRestoredButSavingFails_ShouldOpenItAndReportTheSaveAsANoticeNotAnError()
    {
        var h = Harness();
        h.Store.Path = "saved"; h.Store.SaveFailure = new IOException();
        await using var model = h.Shell();
        await model.InitializeAsync();
        Assert.True(model.Repository.HasRepository);
        Assert.Equal("saved", model.Repository.Snapshot!.Root);
        Assert.True(model.StatusBar.HasNotice);
        Assert.False(model.StatusBar.HasError);
    }

    // Every path answers with the same two-file status, named after the path.
    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Read = (path, _) => Task.FromResult(Snapshot(path));
        return h;
    }
    private static RepositorySnapshot Snapshot(string path) => new(path, "2.43.0", "topic", "abc", null, null, [], RepositoryOperation.None, [],
        [new(new(Encoding.UTF8.GetBytes("partial.txt")), 'M', 'M'), new(new(Encoding.UTF8.GetBytes("new.txt")), '?', '?')]);
}
