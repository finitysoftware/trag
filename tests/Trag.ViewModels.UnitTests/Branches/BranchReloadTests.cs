using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Branches;

// When the branch list is read: it follows the repository's snapshot, not the screens that show it.
public sealed class BranchReloadTests
{
    // A refused switch moved nothing, so the snapshot after it lists the same branches: the list stays as it was,
    // without a read, and still shows what Git left behind.
    [Fact]
    public async Task WhenGitRefusesABranchWrite_ShouldKeepTheListWithoutReadingItAgain()
    {
        var h = new ShellHarness(); h.Branches.Local.Add("topic");
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Your local changes would be overwritten.");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        var reads = h.Branches.Calls;

        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.Local.Single(row => row.Name == "topic"));
        await model.Branches.List.Completion;

        Assert.IsType<BranchRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(reads, h.Branches.Calls);
        Assert.Equal("main", model.Branches.Rows.Current!.Name); Assert.Equal("main", model.Header.BranchText);
        Assert.Contains(model.Branches.Rows.Local, row => row.Name == "topic");
    }

    // A write that runs while the list is read leaves the read without an answer: it ends during the write ("Open the
    // list again") or is dropped when the write completes. The snapshot after the write moved no ref, and the list is
    // read again by it or, at the latest, when the picker opens.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAWriteLeftTheListUnread_ShouldReadItAgainByThePickerAtTheLatest(bool endsDuringTheWrite)
    {
        var h = new ShellHarness(); h.Branches.Local.Add("topic");
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        h.Branches.Gate.IsClosed = true; h.Writer.Gate.IsClosed = true;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await h.Branches.Gate.Started;
        model.Stash.OpenStashCommand.Execute(null); var stash = model.Stash.CreateStashCommand.ExecuteAsync(null); await h.Writer.Gate.Started;
        if (endsDuringTheWrite) { h.Branches.Gate.Open(); await model.Branches.List.Completion; Assert.Equal(Failures.ListChanged, model.Branches.List.Message); }
        h.Writer.Gate.Open(); await stash;
        h.Branches.Gate.Open(); await model.Branches.List.Completion;

        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);

        Assert.Equal("", model.Branches.List.Message);
        Assert.Contains(model.Branches.Rows.Local, row => row.Name == "topic");
    }

    // The list is read when the repository opens and whenever a snapshot moved a ref, local or remote-tracking, or
    // HEAD's branch, by a fetch, a commit or a branch write of any program's; never for a snapshot that moved none, and
    // never for a screen, which shows the list the repository has. Going to a commit reads once more, before the
    // decision it asks for.
    [Fact]
    public async Task WhenASnapshotArrivesOrAScreenOpens_ShouldReadTheListForAMovedRefAndNotForTheScreen()
    {
        var second = Snapshots.Id('b');
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update", second), Snapshots.Commit(second, "Initial")], false, page);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; await model.Branches.List.Completion;
        Assert.Equal(1, h.Branches.Calls);
        h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.Branches.List.Completion;
        Assert.Equal(1, h.Branches.Calls);
        h.Reader.Snapshot = h.Synced() with { RemoteRefs = "fetched" };
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.Branches.List.Completion;
        Assert.Equal(2, h.Branches.Calls);
        h.Reader.Snapshot = h.Reader.Snapshot with { Head = Snapshots.Id('c'), LocalRefs = "committed" };
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.Branches.List.Completion;
        Assert.Equal(3, h.Branches.Calls);

        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); model.Branches.CancelCreateBranchCommand.Execute(null);
        await model.MergeDialog.OpenCommand.ExecuteAsync(null); model.MergeDialog.CancelCommand.Execute(null);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); model.Publish.CancelPublishCommand.Execute(null);
        Assert.Equal(3, h.Branches.Calls);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.Equal(4, h.Branches.Calls); Assert.True(model.GoTo.Target.IsOpen);
    }
}
