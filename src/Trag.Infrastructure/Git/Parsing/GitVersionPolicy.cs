using Trag.Domain.Errors;
using Trag.Domain.Naming;

namespace Trag.Infrastructure.Git.Parsing;

// The oldest Git the application works with. Older versions are refused before anything is read from the repository.
internal static class GitVersionPolicy
{
    public static Version Require(string output)
    {
        var version = GitVersion.Parse(output);
        if (version < GitLimits.MinimumGit) throw new RepositoryException(RepositoryError.GitTooOld);
        return version;
    }
}
