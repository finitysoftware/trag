using Trag.Application.History;
using Trag.Domain.History;
using Trag.Domain.Presentation;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.History;

// A fetch by any program moves a remote-tracking ref while HEAD stays: the sync counts are read again and the shown
// history page is read again in place, with the upstream when only the current branch is shown, keeping the selected
// commit and its details.
public sealed class FetchedRefsTests
{
    private static readonly string Pushed = Snapshots.Id('e');

    [Fact]
    public async Task WhenAFetchMovesTheUpstreamWhileHeadStays_ShouldReadTheCountsAndTheShownPageAgainInPlace()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced() with { RemoteRefs = "before", UpstreamHead = Snapshots.HeadId };
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.History.HistoryAllBranches = false;
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1];
        await model.History.Details.Completion;
        var (pages, details, reads) = (h.History.PageCalls, h.History.DetailCalls, h.Sync.Reads.Count);
        List<HistoryCommit?> selections = [];
        model.Repository.History.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryModel.Selected)) selections.Add(model.Repository.History.Selected); };

        h.Reader.Snapshot = h.Reader.Snapshot with { RemoteRefs = "after", UpstreamHead = Pushed };
        h.History.Commits.Insert(0, Snapshots.Commit(Pushed, "Pushed by someone else", Snapshots.HeadId));
        h.Watcher.Latest.PushReconcile();
        await ShellHarness.Until(() => h.History.PageCalls == pages + 1 && h.Sync.Reads.Count == reads + 1, "the reads after the fetch");
        await model.History.HistoryCompletion; await model.Sync.SyncCompletion;

        Assert.Equal(Pushed, h.History.LastUpstream);
        Assert.Equal([Pushed, Snapshots.HeadId, Snapshots.Id('b')], model.History.Page.Commits.Select(commit => commit.Id));
        Assert.Equal(Snapshots.Id('b'), model.History.SelectedCommit!.Id);
        Assert.DoesNotContain(null, selections);
        Assert.Equal(details, h.History.DetailCalls);
        Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
    }
}
