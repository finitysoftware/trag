namespace Trag.IntegrationTests.Git.Write;

// A rebase, its continue, skip and abort, an edit of commits, and the three resets, each stopped as WriterInterruption
// stops a write.
public sealed class WriterInterruptionRebaseTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.All), "rebase", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesAfterAGitOfTheWriteExited_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopAfterEveryGitAsync(row);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.RefLocks), "rebase", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitHoldsTheRefLocks_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row, string? held) =>
        WriterInterruption.StopWhileGitHoldsTheRefLocksAsync(row, held);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.WorkingTree), "rebase", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitWritesTheWorkingTree_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopWhileGitWritesTheWorkingTreeAsync(row);
}
