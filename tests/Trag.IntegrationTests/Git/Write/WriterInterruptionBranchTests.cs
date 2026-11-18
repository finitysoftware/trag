namespace Trag.IntegrationTests.Git.Write;

// Creating, switching to, checking out, deleting and renaming a branch, creating and deleting a tag, and adding,
// editing and removing a remote, each stopped as WriterInterruption stops a write.
public sealed class WriterInterruptionBranchTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.All), "branch", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesAfterAGitOfTheWriteExited_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopAfterEveryGitAsync(row);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.RefLocks), "branch", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitHoldsTheRefLocks_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row, string? held) =>
        WriterInterruption.StopWhileGitHoldsTheRefLocksAsync(row, held);

    [Theory]
    [MemberData(nameof(WriterInterruptionRows.WorkingTree), "branch", MemberType = typeof(WriterInterruptionRows))]
    public Task WhenCancelArrivesWhileGitWritesTheWorkingTree_ShouldReportWhatTheRepositoryShowsAndLeaveNoLock(string row) =>
        WriterInterruption.StopWhileGitWritesTheWorkingTreeAsync(row);
}
