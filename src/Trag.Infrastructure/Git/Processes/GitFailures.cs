using Trag.Domain.Errors;

namespace Trag.Infrastructure.Git.Processes;

// The failures a write, a synchronization or a clone answers with a sentence instead of letting them escape: the file
// system, a path or an argument, output Trag cannot read, the repository, the process and Git itself. Anything else is
// a defect and propagates.
internal static class GitFailures
{
    public static bool IsExpected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
        or FormatException or RepositoryException or GitCommandException or GitOutputLimitException or System.ComponentModel.Win32Exception;
}
