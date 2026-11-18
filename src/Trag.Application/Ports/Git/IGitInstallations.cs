using Trag.Domain.Setup;

namespace Trag.Application.Ports.Git;

// Finds the Git Trag runs and makes it the one every Git process starts: the chosen git.exe when there is one, otherwise
// the first new enough Git in a folder of the PATH as it stands now, or in a standard install folder.
public interface IGitInstallations
{
    Task<GitFinding> FindAsync(string? chosen, CancellationToken ct);

    // What the Git brings; null when it cannot be asked.
    Task<GitFeatures?> DescribeAsync(GitInstallation git, CancellationToken ct);
}
