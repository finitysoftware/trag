using Trag.Domain.Accounts;
using Trag.Domain.Sync;
using Trag.Infrastructure.Accounts;

namespace Trag.Infrastructure.Git.Sync;

// Everything one synchronization needs to know about a remote: what the user sees (Remote), how refs map, how SSH is
// invoked, the endpoints that are actually used, and the transport options that must be carried to a verified alias.
internal sealed record RemoteConfiguration(SyncRemote Remote, RemoteRefspecMapping[] Mappings,
    string SshCommand, string SshVariant, string EffectiveFetch, string EffectivePush, (string Key, string Value)[] TransportOptions)
{
    public string? AccountId { get; init; }

    // The account this repository keeps for this remote is bound to its two effective endpoints; the store answers for
    // them, and a synchronization carries the answer as AccountId.
    public Task<RemoteAccountState> ReadAccountAsync(RemoteAccountStore store, string root, CancellationToken ct) =>
        store.ReadAsync(root, Remote.Name, EffectiveFetch, EffectivePush, ct);

    public Task SaveAccountAsync(RemoteAccountStore store, string root, string id, CancellationToken ct) =>
        store.SaveAsync(root, Remote.Name, EffectiveFetch, EffectivePush, id, ct);

    public async Task<RemoteConfiguration> WithAccountAsync(RemoteAccountStore store, string root, CancellationToken ct) =>
        this with { AccountId = (await ReadAccountAsync(store, root, ct).ConfigureAwait(false)).SelectedId };
}
