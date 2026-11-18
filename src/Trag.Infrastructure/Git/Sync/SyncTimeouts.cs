using Trag.Domain.Naming;

namespace Trag.Infrastructure.Git.Sync;

// How long a transfer may go without a word from Git before it is stopped (it has no limit of its own, so it runs as long
// as Git reports progress or output), how long the local merge of a pull may take, and how long a verification may take
// afterwards. The verification timeout is its own value on purpose: it must still answer after the operation itself was
// cancelled.
internal static class SyncTimeouts
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(GitLimits.TransferQuietMinutes);
    public static readonly TimeSpan Merge = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Verification = TimeSpan.FromSeconds(20);
}
