using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// The destination could not be read at all, as opposed to being read and found unchanged. The caller explains it.
internal sealed class RemoteReadException(GitResult result) : Exception("The push destination could not be read.")
{
    public GitResult Result { get; } = result;
}
