using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Sync;

// The sidebar's upstream picker: the choices under its search, a choice that closes it, and an open picker that a
// refresh leaves open; and the remote picker, whose choice closes it too. What a choice reads and where it pushes is
// SyncViewModelTests'.
public sealed class SyncPickerViewModelTests
{
    [Fact]
    public async Task WhenABranchIsChosenInTheUpstreamPicker_ShouldCloseItAndListTheRowsOfTheReadThatFollows()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Sync.Selection.Search = "old";
        model.Sync.OpenUpstreamPickerCommand.Execute(null);
        model.Sync.IsUpstreamPickerOpen = true;
        Assert.Equal("", model.Sync.Selection.Search);
        Assert.Equal(model.Sync.Selection.Branches, model.Sync.Selection.Visible);

        model.Sync.Selection.Search = "SHA";
        model.Sync.ChooseBranchCommand.Execute(Assert.Single(model.Sync.Selection.Visible)); await model.Sync.SyncCompletion;

        Assert.False(model.Sync.IsUpstreamPickerOpen);
        Assert.Equal("shared", model.Sync.Selection.SelectedBranch!.Branch.Display);
        Assert.Same(model.Sync.Selection.SelectedBranch, Assert.Single(model.Sync.Selection.Visible));
    }

    [Fact]
    public async Task WhenEnterIsPressedInTheUpstreamSearch_ShouldChooseTheFirstMatchAndNothingWithoutOne()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Sync.IsUpstreamPickerOpen = true;

        model.Sync.Selection.Search = "nothing";
        model.Sync.ChooseFirstBranchCommand.Execute(null); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.IsUpstreamPickerOpen);
        Assert.Equal("trunk", model.Sync.Selection.SelectedBranch!.Branch.Display);

        model.Sync.Selection.Search = "s";
        model.Sync.ChooseFirstBranchCommand.Execute(null); await model.Sync.SyncCompletion;
        Assert.False(model.Sync.IsUpstreamPickerOpen);
        Assert.Equal("shared", model.Sync.Selection.SelectedBranch!.Branch.Display);
    }

    [Fact]
    public async Task WhenASnapshotReplacesTheSelectionWhileTheUpstreamPickerIsOpen_ShouldKeepItOpenOverTheNewRows()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Sync.IsUpstreamPickerOpen = true;
        model.Sync.Selection.Search = "sta";
        var before = model.Sync.Selection;

        backend.Snapshot = backend.Snapshot with { Branch = "other", TrackingRemote = null, UpstreamBranchRef = null };
        backend.SyncGate.IsClosed = true;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.NotSame(before, model.Sync.Selection);
        Assert.Empty(model.Sync.Selection.Visible);

        model.Sync.Selection.RemoteName = "origin"; await backend.SyncGate.Arrival(2);
        backend.SyncGate.Open(); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.IsUpstreamPickerOpen);
        Assert.Equal("sta", model.Sync.Selection.Search);
        Assert.Same(model.Sync.Selection.Branches[2], Assert.Single(model.Sync.Selection.Visible));
    }

    [Fact]
    public async Task WhenARemoteIsChosenInItsPicker_ShouldCloseItAndReadThatRemote()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Sync.IsRemotePickerOpen = true;

        model.Sync.ChooseRemoteCommand.Execute(model.Sync.Selection.RemoteRows[1]); await model.Sync.SyncCompletion;

        Assert.False(model.Sync.IsRemotePickerOpen);
        Assert.Equal("backup", model.Sync.Selection.Remote!.Name);
        Assert.Equal(("backup", null), backend.SyncReads[^1]);
    }

    // Trunk tracks origin/trunk; every read offers trunk, shared and stable on the remote it reads.
    private static FakeSyncWorkspace Backend()
    {
        var backend = new FakeSyncWorkspace
        {
            Snapshot = Snapshots.Create(branch: "trunk", trackingRemote: "origin", upstreamBranchRef: "refs/heads/trunk",
                remotes: [new("origin", "fetch", "push"), new("backup", "backup-fetch", "backup-push")])
        };
        backend.ReadSync = (snapshot, remote, selected) =>
        {
            SyncBranch[] branches = [.. new[] { "trunk", "shared", "stable" }.Select(name => new SyncBranch("refs/heads/" + name, "refs/remotes/" + remote + "/" + name))];
            var source = selected ?? (snapshot.TrackingRemote == remote ? snapshot.UpstreamBranchRef : null);
            return new RepositorySyncState(new(remote, "fetch", "push", "config"), branches, branches.FirstOrDefault(item => item.SourceRef == source), 0, 0, "Local comparison");
        };
        return backend;
    }
}
