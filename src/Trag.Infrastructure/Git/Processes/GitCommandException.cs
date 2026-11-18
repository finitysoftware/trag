namespace Trag.Infrastructure.Git.Processes;

public sealed class GitCommandException(int exitCode) : Exception($"Git failed with exit code {exitCode}.")
{
    public int ExitCode { get; } = exitCode;
}
