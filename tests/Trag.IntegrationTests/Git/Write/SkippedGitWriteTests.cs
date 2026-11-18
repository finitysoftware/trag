namespace Trag.IntegrationTests.Git.Write;

// Every write the interruption matrix knows, run by a Git that skips each command that writes and exits 0 as if it had
// done it, as WriterInterruption.SkipEveryGitWriteAsync runs it.
public sealed class SkippedGitWriteTests
{
    [Theory]
    [MemberData(nameof(WriterInterruptionRows.Every), MemberType = typeof(WriterInterruptionRows))]
    public Task WhenGitExitsZeroWithoutWriting_ShouldNeverReportTheWriteDone(string row) => WriterInterruption.SkipEveryGitWriteAsync(row);
}
