using Trag.Application.Live;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class LoaderTests
{
    [Fact]
    public void WhenNoSnapshotWasApplied_ShouldBeEmpty()
    {
        var (_, _, loader) = Keyed();
        Assert.Empty(loader.Value);
    }

    [Fact]
    public void WhenTheSnapshotHasNoKey_ShouldReadNothingAndStayEmpty()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create());
        Assert.Empty(reads); Assert.Empty(loader.Value);
    }

    [Fact]
    public async Task WhenASnapshotBringsAKey_ShouldReadItsList()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create(stashHead: "first")); await loader.Completion;
        Assert.Equal(["first"], reads); Assert.Equal(["first"], loader.Value);
    }

    [Fact]
    public async Task WhenANewerSnapshotKeepsTheKey_ShouldKeepTheListWithoutReadingAgain()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create(stashHead: "first")); await loader.Completion;
        repository.Apply(Snapshots.Create(stashHead: "first", files: [Snapshots.Modified("a")])); await loader.Completion;
        Assert.Single(reads);
    }

    [Fact]
    public async Task WhenTheKeyChanges_ShouldReadTheNewList()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create(stashHead: "first")); await loader.Completion;
        repository.Apply(Snapshots.Create(stashHead: "second")); await loader.Completion;
        Assert.Equal(["first", "second"], reads); Assert.Equal(["second"], loader.Value);
    }

    [Fact]
    public async Task WhenTheKeyIsGone_ShouldEmptyWithoutReading()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create(stashHead: "first")); await loader.Completion;
        repository.Apply(Snapshots.Create(stashHead: "second")); await loader.Completion;
        repository.Apply(Snapshots.Create()); await loader.Completion;
        Assert.Empty(loader.Value); Assert.Equal(2, reads.Count);
    }

    [Fact]
    public async Task WhenAKeyComesBackAfterItWasGone_ShouldReadAgain()
    {
        var (repository, reads, loader) = Keyed();
        repository.Apply(Snapshots.Create(stashHead: "first")); await loader.Completion;
        repository.Apply(Snapshots.Create(stashHead: "second")); await loader.Completion;
        repository.Apply(Snapshots.Create()); await loader.Completion;
        repository.Apply(Snapshots.Create(stashHead: "third")); await loader.Completion;
        Assert.Equal(["third"], loader.Value); Assert.Equal(3, reads.Count);
    }

    [Fact]
    public void WhenTwoReadsAreOut_ShouldBeBusy()
    {
        var (loader, _) = Pending();
        _ = loader.ReloadAsync();
        _ = loader.ReloadAsync();
        Assert.True(loader.IsBusy);
    }

    [Fact]
    public async Task WhenTheOlderOfTwoReadsAnswersFirst_ShouldDropItsAnswerAndStayBusyForTheNewer()
    {
        var (loader, pending) = Pending();
        var first = loader.ReloadAsync();
        _ = loader.ReloadAsync();
        pending[0].SetResult(["old"]); await first;
        Assert.Empty(loader.Value); Assert.True(loader.IsBusy);
    }

    [Fact]
    public async Task WhenTheNewerOfTwoReadsAnswers_ShouldShowItsAnswerAndBeIdle()
    {
        var (loader, pending) = Pending();
        var first = loader.ReloadAsync();
        var second = loader.ReloadAsync();
        pending[0].SetResult(["old"]); await first;
        pending[1].SetResult(["new"]); await second;
        Assert.Equal(["new"], loader.Value); Assert.False(loader.IsBusy);
    }

    [Fact]
    public async Task WhenClearedDuringARead_ShouldBeIdleAndEmptyAtOnce()
    {
        var (loader, _) = await AnsweredAsync();
        _ = loader.ReloadAsync();
        loader.Clear();
        Assert.False(loader.IsBusy); Assert.Empty(loader.Value);
    }

    [Fact]
    public async Task WhenAReadAnswersAfterAClear_ShouldDropTheAnswer()
    {
        var (loader, pending) = await AnsweredAsync();
        var third = loader.ReloadAsync();
        loader.Clear();
        pending[^1].SetResult(["late"]); await third;
        Assert.Empty(loader.Value); Assert.False(loader.IsBusy);
    }

    [Fact]
    public async Task WhenTheReadAnswers_ShouldShowTheValueWithoutAMessage()
    {
        var loader = Answering(One);
        await loader.ReloadAsync();
        Assert.Equal(["one"], loader.Value); Assert.Equal("", loader.Message);
    }

    [Fact]
    public async Task WhenTheRepositoryMovedUnderTheRead_ShouldKeepTheValueAndSaySo()
    {
        var loader = Answering(One, () => Task.FromResult<IReadOnlyList<string>?>(null));
        await loader.ReloadAsync();
        await loader.ReloadAsync();
        Assert.Equal(["one"], loader.Value); Assert.Equal("Moved.", loader.Message); Assert.False(loader.IsBusy);
    }

    [Fact]
    public async Task WhenTheReadFails_ShouldKeepTheValueAndSaySo()
    {
        var loader = Answering(One, () => throw new IOException("boom"));
        await loader.ReloadAsync();
        await loader.ReloadAsync();
        Assert.Equal(["one"], loader.Value); Assert.Equal("Broken.", loader.Message); Assert.False(loader.IsBusy);
    }

    [Fact]
    public async Task WhenAGoodReadFollowsAFailure_ShouldShowTheNewValueAndTakeTheMessageDown()
    {
        var loader = Answering(One, () => throw new IOException("boom"), () => Task.FromResult<IReadOnlyList<string>?>(["two"]));
        await loader.ReloadAsync();
        await loader.ReloadAsync();
        await loader.ReloadAsync();
        Assert.Equal(["two"], loader.Value); Assert.Equal("", loader.Message);
    }

    [Fact]
    public async Task WhenTheLoaderHasNoRead_ShouldStayEmptyAndIdleOnAReload()
    {
        var repository = new Repository(); repository.Apply(Snapshots.Create());
        var readOnly = new Loader<IReadOnlyList<string>>(repository, read: null, empty: []);
        await readOnly.ReloadAsync();
        Assert.Empty(readOnly.Value); Assert.False(readOnly.IsBusy);
    }

    // A loader that goes before its registry does leaves the shell's Cancel: its read is neither offered nor interrupted.
    [Fact]
    public async Task WhenTheLoaderIsReleased_ShouldLeaveTheShellsCancelWithItsReadRunning()
    {
        var cancels = new CancelRegistry();
        var loader = new Loader<string>("nothing", cancels);
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = loader.LoadAsync(_ => pending.Task);
        Assert.Equal((true, 1), (cancels.CanCancel, cancels.Count));

        loader.Release();
        cancels.CancelAll();

        Assert.Equal((false, 0, true), (cancels.CanCancel, cancels.Count, loader.IsBusy));
        pending.SetResult("one"); await reading;
        Assert.Equal(("one", ""), (loader.Value, loader.Message));
    }

    [Fact]
    public void WhenAnOnDemandReadIsOut_ShouldBeBusyAndShowItsInterim()
    {
        var loader = OnDemand();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = loader.LoadAsync(_ => pending.Task, interim: "loading");
        Assert.True(loader.IsBusy); Assert.Equal("loading", loader.Value);
    }

    [Fact]
    public async Task WhenAnOnDemandReadAnswers_ShouldShowTheAnswerAndBeIdle()
    {
        var loader = OnDemand();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = loader.LoadAsync(_ => pending.Task, interim: "loading");
        pending.SetResult("answer"); await reading;
        Assert.Equal("answer", loader.Value); Assert.False(loader.IsBusy);
    }

    [Fact]
    public async Task WhenAnOnDemandReadFailsWithADomainException_ShouldKeepTheInterimAndLetTheExceptionSpeak()
    {
        var loader = OnDemand();
        await loader.LoadAsync(_ => throw new InvalidDataException("Token expired."), interim: "loading");
        Assert.Equal("loading", loader.Value); Assert.Equal("Token expired.", loader.Message);
    }

    [Fact]
    public async Task WhenAnOnDemandReadFailsOtherwise_ShouldSayTheReadFailed()
    {
        var loader = OnDemand();
        await loader.LoadAsync(_ => throw new IOException("disk"));
        Assert.Equal("Broken.", loader.Message);
    }

    // Git that did not answer in time is neither a broken read nor a cancel the user made.
    [Fact]
    public async Task WhenAnOnDemandReadRunsOutOfTime_ShouldSayGitDidNotAnswerInTime()
    {
        var loader = OnDemand();
        await loader.LoadAsync(_ => throw new TimeoutException());
        Assert.Equal(Failures.TimedOut, loader.Message);
    }

    [Fact]
    public async Task WhenAGoodOnDemandReadFollowsAFailure_ShouldShowItsAnswerAndTakeTheMessageDown()
    {
        var loader = OnDemand();
        await loader.LoadAsync(_ => throw new IOException("disk"));
        await loader.LoadAsync(_ => Task.FromResult<string?>("later"));
        Assert.Equal("later", loader.Value); Assert.Equal("", loader.Message);
    }

    [Fact]
    public void WhenAReadIsInFlight_ShouldOfferTheShellsCancel()
    {
        var (loader, cancels) = Cancelable();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = loader.LoadAsync(_ => pending.Task);
        Assert.True(cancels.CanCancel);
    }

    [Fact]
    public void WhenTheShellCancels_ShouldInterruptTheReadAndBeIdleAtOnce()
    {
        var (loader, cancels) = Cancelable();
        var tokens = new List<CancellationToken>();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = loader.LoadAsync(ct => { tokens.Add(ct); return pending.Task; });
        cancels.CancelAll();
        Assert.True(tokens[0].IsCancellationRequested); Assert.False(loader.IsBusy); Assert.False(cancels.CanCancel);
    }

    [Fact]
    public async Task WhenAReadTheShellInterruptedAnswersLate_ShouldDropTheAnswer()
    {
        var (loader, cancels) = Cancelable();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = loader.LoadAsync(_ => pending.Task);
        cancels.CancelAll();
        pending.SetResult("late"); await reading;
        Assert.Equal("nothing", loader.Value);
    }

    [Fact]
    public async Task WhenAReadEndsInCancellation_ShouldShowTheOwnersCancelText()
    {
        var (loader, _) = Cancelable();
        await loader.LoadAsync(ct => Task.FromCanceled<string?>(ct = new CancellationToken(true)));
        Assert.Equal("Reading canceled.", loader.Message);
    }

    [Fact]
    public async Task WhenAReloadFollowsACanceledRead_ShouldShowTheAnswerAndTakeTheCancelTextDown()
    {
        var (loader, _) = Cancelable();
        await loader.LoadAsync(ct => Task.FromCanceled<string?>(ct = new CancellationToken(true)));
        Assert.Equal("Reading canceled.", loader.Message);
        await loader.ReloadAsync();
        Assert.Equal("fresh", loader.Value); Assert.Equal("", loader.Message);
    }

    [Fact]
    public async Task WhenTheOwnerCancelsARead_ShouldBeIdleAtOnceAndKeepTheValue()
    {
        var (loader, _) = Cancelable();
        await loader.ReloadAsync();
        var second = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = loader.LoadAsync(_ => second.Task);
        loader.Cancel();
        Assert.False(loader.IsBusy); Assert.Equal("fresh", loader.Value);
    }

    [Fact]
    public async Task WhenAReadTheOwnerCanceledAnswersLate_ShouldDropTheAnswerAndSayNothing()
    {
        var (loader, _) = Cancelable();
        await loader.ReloadAsync();
        var second = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = loader.LoadAsync(_ => second.Task);
        loader.Cancel();
        second.SetResult("late again"); await held;
        Assert.Equal("fresh", loader.Value); Assert.Equal("", loader.Message);
    }

    [Fact]
    public async Task WhenALoaderWithoutARepositoryReadsOnDemand_ShouldShowTheAnswer()
    {
        var loader = WithoutARepository(new CancelRegistry());
        await loader.LoadAsync(_ => Task.FromResult<string?>("answer"));
        Assert.Equal("answer", loader.Value);
    }

    [Fact]
    public async Task WhenAReadFailsAfterTheShellInterruptedIt_ShouldReportTheInterruptionNotTheFailure()
    {
        var (loader, cancels, pending, reading) = await ReadingAfterAnAnswerAsync();
        Assert.True(cancels.CanCancel);
        cancels.CancelAll();
        pending.SetException(new IOException("late failure")); await reading;
        Assert.Equal("Canceled.", loader.Message); Assert.Equal("answer", loader.Value); Assert.False(loader.IsBusy);
    }

    // Without a repository nothing else empties the loader.
    [Fact]
    public async Task WhenALoaderWithoutARepositoryIsCleared_ShouldEmptyAndTakeTheMessageDown()
    {
        var (loader, cancels, pending, reading) = await ReadingAfterAnAnswerAsync();
        cancels.CancelAll();
        pending.SetException(new IOException("late failure")); await reading;
        loader.Clear();
        Assert.Equal("nothing", loader.Value); Assert.Equal("", loader.Message);
    }

    // A loader keyed by the stash head over a repository with no snapshot yet; reads collects the key of every read.
    private static (Repository Repository, List<string?> Reads, Loader<IReadOnlyList<string>> Loader) Keyed()
    {
        var repository = new Repository();
        var reads = new List<string?>();
        var loader = new Loader<IReadOnlyList<string>>(repository,
            (snapshot, _) => { reads.Add(snapshot.StashHead); return Task.FromResult<IReadOnlyList<string>?>([snapshot.StashHead!]); },
            empty: [], key: snapshot => snapshot.StashHead);
        return (repository, reads, loader);
    }

    // A loader over a repository with a snapshot; every read waits for a source of its own, added to Pending.
    private static (Loader<IReadOnlyList<string>> Loader, List<TaskCompletionSource<IReadOnlyList<string>?>> Pending) Pending()
    {
        var repository = new Repository(); repository.Apply(Snapshots.Create());
        var pending = new List<TaskCompletionSource<IReadOnlyList<string>?>>();
        var loader = new Loader<IReadOnlyList<string>>(repository, (_, _) =>
        {
            var source = new TaskCompletionSource<IReadOnlyList<string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(source);
            return source.Task;
        }, empty: []);
        return (loader, pending);
    }

    // The same loader, showing the answer of its one read.
    private static async Task<(Loader<IReadOnlyList<string>> Loader, List<TaskCompletionSource<IReadOnlyList<string>?>> Pending)> AnsweredAsync()
    {
        var (loader, pending) = Pending();
        var reading = loader.ReloadAsync();
        pending[0].SetResult(["new"]); await reading;
        return (loader, pending);
    }

    private static Task<IReadOnlyList<string>?> One() => Task.FromResult<IReadOnlyList<string>?>(["one"]);

    // A loader over a repository with a snapshot whose reads give the answers in turn.
    private static Loader<IReadOnlyList<string>> Answering(params Func<Task<IReadOnlyList<string>?>>[] answers)
    {
        var repository = new Repository(); repository.Apply(Snapshots.Create());
        var next = 0;
        return new Loader<IReadOnlyList<string>>(repository, (_, _) => answers[next++](), empty: []) { ChangedMessage = "Moved.", FailedMessage = "Broken." };
    }

    // A loader with no read of its own, over a repository with no snapshot, which an on-demand read does not need; an
    // invalid-data failure describes itself.
    private static Loader<string> OnDemand() =>
        new(new Repository(), read: null, empty: "nothing")
        {
            FailedMessage = "Broken.", DescribeFailure = error => error is InvalidDataException data ? data.Message : "Broken."
        };

    // A loader over a repository with a snapshot, registered for the shell's cancel; its reload answers "fresh".
    private static (Loader<string> Loader, CancelRegistry Cancels) Cancelable()
    {
        var repository = new Repository(); repository.Apply(Snapshots.Create());
        var cancels = new CancelRegistry();
        var loader = new Loader<string>(repository, (_, _) => Task.FromResult<string?>("fresh"), empty: "nothing", cancels: cancels) { CanceledMessage = "Reading canceled." };
        return (loader, cancels);
    }

    private static Loader<string> WithoutARepository(CancelRegistry cancels) =>
        new("nothing", cancels) { CanceledMessage = "Canceled.", FailedMessage = "Broken." };

    // A loader without a repository that shows "answer" and has a second read in flight, waiting for Pending.
    private static async Task<(Loader<string> Loader, CancelRegistry Cancels, TaskCompletionSource<string?> Pending, Task Reading)> ReadingAfterAnAnswerAsync()
    {
        var cancels = new CancelRegistry();
        var loader = WithoutARepository(cancels);
        await loader.LoadAsync(_ => Task.FromResult<string?>("answer"));
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = loader.LoadAsync(_ => pending.Task);
        return (loader, cancels, pending, reading);
    }
}
