using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Stash;

// The stash list follows refs/stash; whatever the user chose in it has to follow the list.
public sealed class StashListTests
{
    [Fact]
    public async Task WhenAChosenStashVanishesFromTheList_ShouldDropTheChoice()
    {
        var h = new ShellHarness();
        var older = h.Stashes.Add("On main: older"); var newer = h.Stashes.Add("On main: newer");
        h.Reader.Snapshot = Snapshots.Create(stashHead: h.Stashes.Head);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        Assert.Equal(2, model.Stash.Rows.Count);
        model.Stash.RequestDropCommand.Execute(model.Stash.Rows.Single(row => row.Id == newer.Id));
        Assert.True(model.Stash.Drop.IsOpen);

        // Git's own tools dropped that entry: refs/stash moves, the list reloads, and the confirmation has no subject left.
        h.Stashes.Remove(newer.Id);
        h.Reader.Snapshot = Snapshots.Create(stashHead: h.Stashes.Head);
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion;
        var remaining = Assert.Single(model.Stash.Rows);
        Assert.Equal(older.Id, remaining.Id); Assert.False(model.Stash.Drop.IsOpen); Assert.Null(model.Stash.Drop.Subject);

        await model.Stash.PreviewCommand.ExecuteAsync(remaining);
        Assert.True(model.Stash.IsStashPreview); Assert.True(model.History.Details.HasSubject);
        h.Stashes.Remove(older.Id);
        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion;
        Assert.False(model.Stash.HasStashes); Assert.Empty(model.Stash.Rows);
        Assert.Null(model.Stash.SelectedStash); Assert.False(model.Stash.IsStashPreview); Assert.False(model.History.Details.HasSubject);
    }
}
