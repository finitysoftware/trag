using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.TestSupport;
using Trag.ViewModels.Sync;

namespace Trag.ViewModels.UnitTests.Sync;

// The target as the two selectors show it: what the target applies stays quiet, each snapshot's selection owns its own
// choice, and the rows are items of the list. When the target is read and kept is the repository's remotes'
// (Application's RemotesModelTests, Domain's SyncTargetSelectionTests).
public sealed class SyncSelectionTests
{
    [Fact]
    public void WhenTheSelectionIsShownOrChangedByTheUser_ShouldReportOnlyTheUsersChanges()
    {
        var target = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var selection = SyncSelection.Empty().Following(target, SyncBranchChoice.None);
        var changes = 0;
        selection.Changed += () => changes++;
        selection.Show(target, Choice(SyncViewModelTests.State("origin", "refs/heads/trunk")));
        Assert.Equal(0, changes);
        Assert.NotNull(selection.SelectedBranch);

        selection.RemoteName = selection.RemoteNames[1];
        Assert.Equal(1, changes);
        Assert.Empty(selection.Branches);
        selection.Show(target with { RemoteName = "backup" }, Choice(SyncViewModelTests.State("backup", null)));
        Assert.Equal(1, changes);
        selection.SelectedBranch = selection.Branches[0];
        Assert.Equal(2, changes);
    }

    [Fact]
    public void WhenAnEqualSnapshotFollows_ShouldKeepTheRowsWhileEachSelectionOwnsItsBranchChoice()
    {
        var target = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var choice = Choice(SyncViewModelTests.State("origin", "refs/heads/trunk"));
        var first = SyncSelection.Empty().Following(target, choice);
        var shown = target.WithSnapshot(Snapshot() with { Files = [Snapshots.Modified("changed.txt")] });
        var next = first.Following(shown, choice);
        Assert.NotSame(first, next);
        Assert.Same(first.SelectedBranch, next.SelectedBranch);
        var changed = 0;
        next.Changed += () => changed++;
        first.SelectedBranch = null;
        Assert.NotNull(next.SelectedBranch);
        Assert.Equal(0, changed);

        // A new read's equal-looking branch is a new row.
        var kept = next.SelectedBranch;
        next.Show(shown, Choice(SyncViewModelTests.State("origin", "refs/heads/trunk")));
        Assert.NotSame(kept, next.SelectedBranch); Assert.Equal(kept!.Branch, next.SelectedBranch!.Branch);
    }

    [Fact]
    public void WhenTheBranchesAreShown_ShouldSelectAnItemOfTheListEvenForABoundedOutCurrentBranch()
    {
        var state = SyncViewModelTests.State("origin", "refs/heads/trunk");
        var selection = SyncSelection.Empty();
        selection.Show(SyncTargetSelection.Empty, Choice(state));
        Assert.Same(Assert.Single(selection.Branches), selection.SelectedBranch);
        selection.Show(SyncTargetSelection.Empty, Choice(state with { Branches = [] }));
        Assert.Same(Assert.Single(selection.Branches), selection.SelectedBranch);
        Assert.Equal(state.Branch, selection.SelectedBranch!.Branch);
    }

    [Fact]
    public void WhenABranchIsChosen_ShouldMarkThatRowAloneAsChosen()
    {
        var selection = SyncSelection.Empty();
        selection.Show(SyncTargetSelection.Empty, new(Branches("trunk", "shared"), null));
        Assert.Equal([false, false], selection.Branches.Select(row => row.IsChosen));
        selection.Show(SyncTargetSelection.Empty, new(Branches("trunk", "shared"), new("refs/heads/trunk", "refs/remotes/origin/trunk")));
        Assert.Equal([true, false], selection.Branches.Select(row => row.IsChosen));

        selection.SelectedBranch = selection.Branches[1];
        Assert.Equal([false, true], selection.Branches.Select(row => row.IsChosen));
        selection.SelectedBranch = null;
        Assert.Equal([false, false], selection.Branches.Select(row => row.IsChosen));
    }

    // Nothing chosen yet: each selector asks for its choice.
    [Fact]
    public void WhenNothingIsChosen_ShouldAskForTheRemoteAndTheBranch()
    {
        var selection = SyncSelection.Empty();
        Assert.Equal(("Choose remote", "Choose branch"), (selection.RemoteLabel, selection.BranchLabel));
        selection.Show(SyncTargetSelection.Empty, new(Branches("trunk"), null));
        selection.SelectedBranch = selection.Branches[0];
        Assert.Equal("trunk", selection.BranchLabel);
    }

    [Fact]
    public void WhenTheBranchesAreFiltered_ShouldKeepTheRowsWhoseLabelHoldsTheTrimmedTextInAnyCase()
    {
        var selection = SyncSelection.Empty();
        selection.Show(SyncTargetSelection.Empty, new(Branches("trunk", "shared", "stable"), null));

        selection.Search = " S ";
        Assert.Equal(["shared", "stable"], selection.Visible.Select(row => row.Label));
        selection.Search = " ";
        Assert.Equal(selection.Branches, selection.Visible);
    }

    [Fact]
    public void WhenARemoteIsChosen_ShouldMarkItsRowAloneAsChosenAndSaySo()
    {
        var target = SyncTargetSelection.Empty.WithSnapshot(Snapshot());
        var selection = SyncSelection.Empty().Following(target, SyncBranchChoice.None);
        Assert.Equal([("origin", true), ("backup", false)], selection.RemoteRows.Select(row => (row.Name, row.IsChosen)));
        var renewed = 0;
        selection.PropertyChanged += (_, e) => renewed += e.PropertyName == nameof(SyncSelection.RemoteRows) ? 1 : 0;

        selection.RemoteName = "backup";
        Assert.Equal([("origin", false), ("backup", true)], selection.RemoteRows.Select(row => (row.Name, row.IsChosen)));
        Assert.Equal(1, renewed);
    }

    private static SyncBranch[] Branches(params string[] names) =>
        [.. names.Select(name => new SyncBranch("refs/heads/" + name, "refs/remotes/origin/" + name))];

    private static SyncBranchChoice Choice(RepositorySyncState state) => new(state.Choices, state.Branch);

    private static RepositorySnapshot Snapshot() => Snapshots.Create(branch: "trunk", trackingRemote: "origin",
        upstreamBranchRef: "refs/heads/trunk", remotes: [new("origin", "fetch", "push"), new("backup", "backup-fetch", "backup-push")]);
}
