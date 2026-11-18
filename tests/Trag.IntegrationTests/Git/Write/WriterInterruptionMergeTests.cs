namespace Trag.IntegrationTests.Git.Write;

// A branch merge as a fast-forward, with a merge commit or with conflicts, its abort and its finish, and the choices on
// its conflicts, each stopped as WriterInterruption stops a write.
public sealed class WriterInterruptionMergeTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.All), "merge", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesAfterAGitOfTheWriteExited_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopAfterEveryGitAsync(row);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.RefLocks), "merge", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitHoldsTheRefLocks_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row, string? held) =>
        WriterInterruption.StopWhileGitHoldsTheRefLocksAsync(row, held);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.WorkingTree), "merge", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitWritesTheWorkingTree_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopWhileGitWritesTheWorkingTreeAsync(row);
}
