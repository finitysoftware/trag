using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Diagnostics;

// Git refuses a repository whose owner is another account (safe.directory) in the same words for every command.
internal static class GitOwnership
{
    public static bool Refused(GitResult result) => Encoding.UTF8.GetString(result.StandardError).Contains("dubious ownership", StringComparison.Ordinal);
}
