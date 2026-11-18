namespace Trag.Infrastructure.Git.Processes;

public sealed record GitResult(int ExitCode, byte[] StandardOutput, byte[] StandardError, bool OutputTruncated = false)
{
    // A secret of the run that only the details shown to the user leave out; what Trag reads keeps Git's own words.
    public Func<string, string>? HideInDetails { get; init; }
}
