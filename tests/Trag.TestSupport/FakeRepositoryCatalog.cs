using Trag.Application.Ports.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;

namespace Trag.TestSupport;

public sealed class FakeRepositoryCatalog : IRepositoryCatalog
{
    public List<RepositoryEntry> Entries { get; } = [];
    public IReadOnlyList<RepositoryEntry>? Saved { get; private set; }
    public int Saves { get; private set; }
    public Dictionary<string, RepositoryProbe> Probes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Initialized { get; } = [];
    public AsyncGate InitializeGate { get; } = new();
    public List<AccountInfo> Accounts { get; } = [];
    public Exception? AccountsError { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<AccountInfo>>>? ReadAccounts { get; set; }
    public int AccountsReads { get; private set; }
    public AsyncGate AccountsGate { get; } = new();
    public Dictionary<string, IReadOnlyList<RemoteRepository>> Remote { get; } = [];
    // Replaces the listed repositories of an account, so a test can hold one account's read.
    public Func<string, CancellationToken, Task<RemoteRepositoryList>>? ReadRemote { get; set; }
    // Replaces the link check of a path, so a test can hold one link while the others answer.
    public Func<string, CancellationToken, Task<RepositoryProbe>>? ReadProbe { get; set; }
    public Dictionary<string, IReadOnlyList<PublishOwner>> Owners { get; } = [];
    public Exception? OwnersError { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<PublishOwner>>>? ReadOwners { get; set; }
    public List<CloneRequest> Clones { get; } = [];
    public CloneResult CloneOutcome { get; set; } = new(true, "Repository cloned.");
    public AsyncGate CloneGate { get; } = new();
    public string HomeFolder { get; set; } = Path.Combine(Path.GetTempPath(), "trag-home");
    // Failures of the list's store and of Git's init, thrown as the real ones would.
    public Exception? LoadError { get; set; }
    public Exception? SaveError { get; set; }
    public Exception? InitializeError { get; set; }

    public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) =>
        LoadError is { } error ? Task.FromException<IReadOnlyList<RepositoryEntry>>(error) : Task.FromResult<IReadOnlyList<RepositoryEntry>>(Entries.ToArray());
    public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct)
    {
        if (SaveError is { } error) return Task.FromException<IReadOnlyList<RepositoryEntry>>(error);
        Saved = change(Saved ?? Entries.ToArray()); Saves++;
        return Task.FromResult(Saved);
    }
    public Task<RepositoryProbe> ProbeAsync(string path, CancellationToken ct) => ReadProbe is { } read ? read(path, ct) :
        Task.FromResult(Probes.TryGetValue(path, out var probe) ? probe : new RepositoryProbe(RepositoryLinkStatus.Valid, "main", null, path));
    public async Task InitializeAsync(string path, CancellationToken ct)
    {
        await InitializeGate.WaitAsync(ct).ConfigureAwait(false);
        if (InitializeError is { } error) throw error;
        Initialized.Add(path);
        Probes[path] = new(RepositoryLinkStatus.Valid, "main", null, path);
    }
    public async Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct)
    {
        AccountsReads++;
        if (ReadAccounts is { } read) return await read(ct).ConfigureAwait(false);
        await AccountsGate.WaitAsync(ct).ConfigureAwait(false);
        if (AccountsError is { } error) throw error;
        return Accounts.ToArray();
    }
    public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct) => ReadRemote is { } read ? read(accountId, ct) :
        Task.FromResult(new RemoteRepositoryList(Remote.GetValueOrDefault(accountId) ?? [], ReachedLimit: false));
    public Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) =>
        ReadOwners is { } read ? read(accountId, ct) :
        OwnersError is { } error ? Task.FromException<IReadOnlyList<PublishOwner>>(error) : Task.FromResult(Owners.GetValueOrDefault(accountId) ?? []);
    public async Task<CloneResult> CloneAsync(CloneRequest request, CancellationToken ct)
    {
        Clones.Add(request);
        await CloneGate.WaitAsync(ct).ConfigureAwait(false);
        if (CloneOutcome.Succeeded) Probes[request.TargetFolder] = new(RepositoryLinkStatus.Valid, "main", request.CloneUrl, request.TargetFolder);
        return CloneOutcome;
    }
}
