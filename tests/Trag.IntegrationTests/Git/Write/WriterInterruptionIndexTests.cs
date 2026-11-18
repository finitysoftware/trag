namespace Trag.IntegrationTests.Git.Write;

// Staging, unstaging, a hunk, a discard, a commit, an amend, an ignore line and a file's version from history, each
// stopped as WriterInterruption stops a write.
public sealed class WriterInterruptionIndexTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.All), "index", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesAfterAGitOfTheWriteExited_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopAfterEveryGitAsync(row);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.RefLocks), "index", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitHoldsTheRefLocks_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row, string? held) =>
        WriterInterruption.StopWhileGitHoldsTheRefLocksAsync(row, held);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.WorkingTree), "index", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitWritesTheWorkingTree_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopWhileGitWritesTheWorkingTreeAsync(row);
}
