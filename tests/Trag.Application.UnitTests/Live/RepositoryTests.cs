using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class RepositoryTests
{
    [Fact]
    public void WhenNoSnapshotWasApplied_ShouldHaveNoRepository()
    {
        var (state, _, _) = Subscribed();
        Assert.False(state.HasRepository); Assert.Null(state.Snapshot);
    }

    [Fact]
    public void WhenTheFirstSnapshotIsApplied_ShouldHaveTheRepositoryAndHandTheSnapshotOnWithoutAPreviousOne()
    {
        var (state, applied, _) = Subscribed();
        var first = Snapshots.Create();
        state.Apply(first);
        Assert.True(state.HasRepository); Assert.Same(first, state.Snapshot); Assert.Equal([(first, (RepositorySnapshot?)null)], applied);
    }

    [Fact]
    public void WhenANewerSnapshotIsApplied_ShouldHandItOnWithThePreviousOne()
    {
        var (state, applied, _) = Subscribed();
        var first = Snapshots.Create(); var second = Snapshots.Create(files: [Snapshots.Modified("a")]);
        state.Apply(first);
        state.Apply(second);
        Assert.Same(second, state.Snapshot); Assert.Same(first, applied[1].Previous);
    }

    // Settled comes for each snapshot only after every subscriber had it; the check runs inside the Settled handler.
    [Fact]
    public void WhenSnapshotsAreApplied_ShouldSettleEachAfterItWasHandedOn()
    {
        var (state, applied, settled) = Subscribed();
        var first = Snapshots.Create(); var second = Snapshots.Create(files: [Snapshots.Modified("a")]);
        state.Apply(first);
        state.Apply(second);
        Assert.Equal(applied, settled);
    }

    [Fact]
    public void WhenTheRepositoryHasNoWriter_ShouldAllowNoWrite()
    {
        var readOnly = new Repository();
        readOnly.Apply(Snapshots.Create());
        Assert.False(readOnly.HasWriter); Assert.False(readOnly.IsWritable); Assert.False(readOnly.CanWrite);
    }

    [Fact]
    public void WhenAWriterHasNoRepositoryYet_ShouldNotBeWritable()
    {
        var state = new Repository(hasWriter: true);
        Assert.False(state.IsWritable);
    }

    [Fact]
    public void WhenAWriterHasAnIdleRepository_ShouldAllowOrdinaryWritesButNoMerge()
    {
        var state = Writable();
        Assert.True(state.IsWritable); Assert.True(state.CanWrite); Assert.False(state.CanMerge);
    }

    [Fact]
    public void WhenTheRepositoryIsBeingRead_ShouldBeBusyAndNotWritable()
    {
        var state = Writable();
        state.IsReading = true;
        Assert.True(state.IsBusy); Assert.False(state.IsWritable);
    }

    [Fact]
    public void WhenAWriteRuns_ShouldBeBusyAndNotWritable()
    {
        var state = Writable();
        state.IsWriting = true;
        Assert.True(state.IsBusy); Assert.False(state.IsWritable);
    }

    [Fact]
    public void WhenTheLastWriteNeedsReview_ShouldNotBeWritableThoughNotBusy()
    {
        var state = Writable();
        state.NeedsReview = true;
        Assert.False(state.IsBusy); Assert.False(state.IsWritable);
    }

    [Fact]
    public void WhenAnOperationTragDoesNotHandleIsUnderway_ShouldStayWritableInPrincipleButAllowNoOrdinaryWriteOrMerge()
    {
        var state = Writable();
        state.Apply(Snapshots.Create(operation: RepositoryOperation.Rebase));
        Assert.True(state.IsWritable); Assert.False(state.CanWrite); Assert.False(state.CanMerge);
    }

    // A merge or a rebase starts from a branch with commits, over a repository Trag can write now.
    [Fact]
    public void WhenTheRepositoryIsWritableOnABranchWithCommits_ShouldLetAMergeOrRebaseStartAndOtherwiseNot()
    {
        var state = Writable();
        Assert.True(state.CanStartOperation);
        state.IsWriting = true;
        Assert.False(state.CanStartOperation);
        state.IsWriting = false;
        state.Apply(Snapshots.Create(unborn: true));
        Assert.False(state.CanStartOperation);
    }

    [Fact]
    public void WhenAMergeTragUnderstandsIsUnderway_ShouldAllowMergeWritesButNoOrdinaryOnes()
    {
        var state = Writable();
        state.Apply(Snapshots.Create(operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge", "fp", "")));
        Assert.False(state.CanWrite); Assert.True(state.CanMerge);
    }

    // Everything a command may read here changes in one step, so each change is one pass over the tracked commands.
    [Fact]
    public void WhenASnapshotIsApplied_ShouldRequeryOnceAndAnnounceWhatItChanged()
    {
        var passes = 0;
        var (state, derived) = Tracked(() => passes++);
        state.Apply(Snapshots.Create());
        Assert.Equal(1, passes);
        Assert.Equal(["Header", "HasRepository", "CanWrite"], derived);
    }

    [Fact]
    public void WhenAWriteStarts_ShouldRequeryOnceAndAnnounceWhatItChanged()
    {
        var passes = 0;
        var (state, derived) = Tracked(() => passes++);
        state.Apply(Snapshots.Create());
        derived.Clear();
        state.IsWriting = true;
        Assert.Equal(2, passes);
        Assert.Equal(["IsBusy", "CanWrite", "IsWriting"], derived);
    }

    [Fact]
    public void WhenAFlagIsSetToWhatItAlreadyIs_ShouldNotRequery()
    {
        var passes = 0;
        var (state, _) = Tracked(() => passes++);
        state.Apply(Snapshots.Create());
        state.IsWriting = true;
        Assert.Equal(2, passes);
        state.IsWriting = true;
        Assert.Equal(2, passes);
    }

    [Fact]
    public void WhenTheLastWriteNeedsReview_ShouldRequeryOnce()
    {
        var passes = 0;
        var (state, _) = Tracked(() => passes++);
        state.Apply(Snapshots.Create());
        state.IsWriting = true;
        state.NeedsReview = true;
        Assert.Equal(3, passes);
    }

    // The refresh says so to whatever reads what the snapshot does not carry.
    [Fact]
    public void WhenARefreshCompletes_ShouldAnswerThePendingReviewInOneRequeryAndSaySo()
    {
        var passes = 0;
        var (state, _) = Tracked(() => passes++);
        state.Apply(Snapshots.Create());
        state.IsWriting = true;
        state.NeedsReview = true;
        state.CompleteRefresh();
        Assert.False(state.NeedsReview); Assert.Equal(4, passes);
    }

    // The sidebar shows an operation's pill while the repository says it is in progress, through its announcement: the pill
    // comes when the operation begins, stays while Git stops at another step, and goes once a snapshot without it is applied.
    [Theory]
    [InlineData(RepositoryOperation.Merge, nameof(Repository.HasMerge))]
    [InlineData(RepositoryOperation.Rebase, nameof(Repository.HasRebase))]
    [InlineData(RepositoryOperation.Conflict, nameof(Repository.HasStashConflicts))]
    [InlineData(RepositoryOperation.Revert, nameof(Repository.HasPick))]
    [InlineData(RepositoryOperation.CherryPick, nameof(Repository.HasPick))]
    public void WhenAnOperationBeginsGoesOnAndEnds_ShouldShowItsPillUntilExactlyTheSnapshotWithoutIt(RepositoryOperation operation, string pill)
    {
        var state = new Repository();
        state.Apply(Snapshots.Create());
        var announced = new List<string?>();
        state.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        state.Apply(Snapshots.Create(operation: operation));
        Assert.Contains(pill, announced); Assert.True(Shows());
        state.Apply(Snapshots.Create(operation: operation, files: [Snapshots.Conflict("a.txt")]));
        Assert.True(Shows());
        announced.Clear();
        state.Apply(Snapshots.Create());
        Assert.Contains(pill, announced); Assert.False(Shows());

        bool Shows() => state.HasMerge || state.HasRebase || state.HasStashConflicts || state.HasPick;
    }

    // A repository whose applied and settled snapshots are recorded with the snapshot before each.
    private static (Repository State, List<(RepositorySnapshot Snapshot, RepositorySnapshot? Previous)> Applied, List<(RepositorySnapshot Snapshot, RepositorySnapshot? Previous)> Settled) Subscribed()
    {
        var state = new Repository();
        var applied = new List<(RepositorySnapshot Snapshot, RepositorySnapshot? Previous)>();
        var settled = new List<(RepositorySnapshot Snapshot, RepositorySnapshot? Previous)>();
        state.SnapshotApplied += (snapshot, previous) => applied.Add((snapshot, previous));
        state.Settled += (snapshot, previous) => { Assert.Equal(settled.Count + 1, applied.Count); settled.Add((snapshot, previous)); };
        return (state, applied, settled);
    }

    // A repository with a writer and a snapshot, nothing running and nothing to review.
    private static Repository Writable()
    {
        var state = new Repository(hasWriter: true);
        state.Apply(Snapshots.Create());
        return state;
    }

    // A repository with a writer and one tracked command; pass is called on every requery pass, and the names of the
    // properties the repository announces are collected.
    private static (Repository State, List<string> Derived) Tracked(Action pass)
    {
        var state = new Repository(hasWriter: true);
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => pass();
        state.Requery.Track(command);
        var derived = new List<string>();
        ((System.ComponentModel.INotifyPropertyChanged)state).PropertyChanged += (_, e) => derived.Add(e.PropertyName!);
        return (state, derived);
    }
}
