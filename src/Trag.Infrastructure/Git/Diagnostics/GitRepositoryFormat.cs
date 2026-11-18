using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Diagnostics;

// Git refuses a repository that uses a format extension it does not know, as Git 2.43 refuses a reftable repository, in
// the same words for every command.
internal static class GitRepositoryFormat
{
    public static bool Unknown(GitResult result) => Encoding.UTF8.GetString(result.StandardError).Contains("unknown repository extension", StringComparison.Ordinal);
}
