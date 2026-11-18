using Trag.Application.Ports.Git;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;

namespace Trag.Infrastructure.Git.Sync;

// What has to hold before a remote is contacted to change one of its refs: the repository is still the one shown, the
// remote is still there and usable, and the account it maps to is known. The caller has checked its own name already
// and says what it is doing and what a remote that went means for it.
internal sealed class RemoteWriteCheck(IRepositoryReader reader, RemoteConfigurationReader configuration, RemoteAccountStore? accountStore)
{
    public async Task<(GitWriteResult? Rejected, RemoteConfiguration? Config)> PrepareAsync(RepositorySnapshot expected, string remote,
        string doing, string remoteGone, CancellationToken ct)
    {
        var root = expected.Root;
        var fresh = await reader.ReadAsync(root, ct).ConfigureAwait(false);
        if (!SnapshotComparison.ReadyForSync(expected, fresh))
            return (GitWriteResult.Rejected(SyncOutcomeText.StateUnsupported(doing)), null);
        RemoteConfiguration config;
        try { config = await configuration.ReadConfigurationAsync(fresh, remote, ct).ConfigureAwait(false); }
        catch (ArgumentException) { return (GitWriteResult.Rejected(remoteGone), null); }
        if (config.Remote.Limitation.Length != 0) return (GitWriteResult.Rejected(config.Remote.Limitation), null);
        if (accountStore is not null) config = await config.WithAccountAsync(accountStore, root, ct).ConfigureAwait(false);
        return (null, config);
    }
}
