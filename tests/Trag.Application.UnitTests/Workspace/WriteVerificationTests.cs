using Trag.Application.Workspace;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class WriteVerificationTests
{
    private static readonly GitWriteResult Done = new(GitWriteOutcome.Succeeded, "Done.");

    [Fact]
    public async Task WhenTheVerificationReadSucceeds_ShouldShowItsSnapshotAndCarryItOnTheResult()
    {
        var (state, slot, _) = OpenedSlot.Create();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]) };
        var result = await new WriteVerification(reader, state).VerifyAsync(slot, Done);
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome); Assert.Equal("Done.", result.Message);
        Assert.NotNull(result.Snapshot); Assert.Same(result.Snapshot, slot.Snapshots.Snapshot); Assert.True(state.Holds(result.Snapshot!));
        Assert.Equal("fixture", reader.LastPath);
    }

    [Fact]
    public async Task WhenTheVerificationReadFails_ShouldMakeTheOutcomeUncertainAndKeepTheShownSnapshot()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var reader = new FakeRepositoryReader { Failure = new IOException("index.lock") };
        var result = await new WriteVerification(reader, state).VerifyAsync(slot, Done);
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
        Assert.Equal("Done. The final status could not be read; refresh before continuing.", result.Message);
        Assert.Null(result.Snapshot); Assert.Same(shown, slot.Snapshots.Snapshot);
    }

    [Fact]
    public async Task WhenTheVerificationFailsForAResultCarryingASnapshot_ShouldDropTheSnapshotButKeepTheWriteDetails()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var reader = new FakeRepositoryReader { Failure = new IOException() };
        var unverified = new GitWriteResult(GitWriteOutcome.Succeeded, "Created.", "details", "commit", shown);
        var result = await new WriteVerification(reader, state).VerifyAsync(slot, unverified);

        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
        Assert.Null(result.Snapshot);
        Assert.Equal("details", result.Details);
        Assert.Equal("commit", result.CommitId);
        Assert.Same(shown, slot.Snapshots.Snapshot);
    }

    // The read's token comes from the verification's own budget, not from the caller of the write.
    [Fact]
    public async Task WhenTheVerificationReadOutlastsItsBudget_ShouldMakeTheOutcomeUncertain()
    {
        var (state, slot, _) = OpenedSlot.Create();
        var reader = new FakeRepositoryReader { Gate = { IsClosed = true } };
        var verifying = new WriteVerification(reader, state, TimeSpan.FromMilliseconds(100)).VerifyAsync(slot, Done);
        await reader.Gate.Started;
        Assert.True(reader.Gate.Token.CanBeCanceled);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = reader.Gate.Token.Register(() => expired.SetResult());
        await expired.Task;
        reader.Gate.Cancel(); // the fake only records its token; the budget expiring is what a real reader would observe
        var result = await verifying;
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
    }

    [Fact]
    public async Task WhenTheSessionIsReplacedDuringTheVerification_ShouldReturnTheSnapshotWithoutStoringIt()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var reader = new FakeRepositoryReader { Gate = { IsClosed = true } };
        var verifying = new WriteVerification(reader, state).VerifyAsync(slot, Done);
        await reader.Gate.Started;
        var next = state.Replace("other", out _);
        reader.Gate.Open();
        var result = await verifying;
        Assert.NotNull(result.Snapshot);
        Assert.Same(shown, slot.Snapshots.Snapshot); Assert.Null(next.Snapshots.Snapshot);
    }
}
