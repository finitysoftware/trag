using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Diagnostics;

// Under safe.bareRepository=explicit Git refuses a bare repository it is not pointed at, before it answers anything, in
// the same words for every command.
internal static class GitBareRepository
{
    public static bool Refused(GitResult result) => Encoding.UTF8.GetString(result.StandardError).Contains("cannot use bare repository", StringComparison.Ordinal);
}
