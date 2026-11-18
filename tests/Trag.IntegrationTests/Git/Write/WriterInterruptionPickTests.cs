namespace Trag.IntegrationTests.Git.Write;

// A cherry-pick and a revert, their continue and abort, and a stash pushed, applied and dropped, each stopped as
// WriterInterruption stops a write.
public sealed class WriterInterruptionPickTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.All), "pick", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesAfterAGitOfTheWriteExited_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopAfterEveryGitAsync(row);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.RefLocks), "pick", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitHoldsTheRefLocks_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row, string? held) =>
        WriterInterruption.StopWhileGitHoldsTheRefLocksAsync(row, held);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.WorkingTree), "pick", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitWritesTheWorkingTree_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopWhileGitWritesTheWorkingTreeAsync(row);
}
