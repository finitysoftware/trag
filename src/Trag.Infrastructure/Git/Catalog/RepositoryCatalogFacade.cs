using Trag.Application.Ports.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.Git.Catalog;

// Links, link checks, git init and clone for the repository picker. Removing a link never deletes files.
public sealed class RepositoryCatalogFacade : IRepositoryCatalog
{
    private readonly IRepositoryCatalogStore store;
    private readonly IRemoteRepositoryDirectory? accounts;
    private readonly GitRepositoryProbe probe;
    private readonly GitProcessRunner runner;
    private readonly GitCloneOperation clone;

    // quietLimit is Git's transfer quiet limit unless a test sets a shorter one.
    public RepositoryCatalogFacade(GitProcessRunner runner, IRepositoryCatalogStore store, IGitAccounts? accounts = null, string? askPassExecutable = null,
        TimeSpan? quietLimit = null)
    {
        this.store = store;
        this.accounts = accounts;
        probe = new(runner);
        this.runner = runner;
        clone = new(runner, probe, accounts, askPassExecutable, quietLimit ?? SyncTimeouts.Quiet);
    }

    public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) => store.LoadAsync(ct);
    public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct) =>
        store.UpdateAsync(change, ct);

    public Task<RepositoryProbe> ProbeAsync(string path, CancellationToken ct) => probe.ProbeAsync(path, ct);

    public string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // A new, empty repository where the user asked for one. Git decides the initial branch and the defaults, not Trag.
    public Task InitializeAsync(string path, CancellationToken ct) => Task.Run(async () =>
    {
        Directory.CreateDirectory(path);
        var result = await runner.RunAsync(path, ["init"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new GitCommandException(result.ExitCode);
    }, ct);

    public Task<CloneResult> CloneAsync(CloneRequest request, CancellationToken ct) => clone.CloneAsync(request, ct);

    public async Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct) =>
        accounts is null ? [] : await accounts.AccountsAsync(ct).ConfigureAwait(false);

    public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct) =>
        accounts is null ? Task.FromResult(new RemoteRepositoryList([], ReachedLimit: false)) : accounts.ListRemoteAsync(accountId, ct);

    public Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) =>
        accounts is null ? Task.FromResult<IReadOnlyList<PublishOwner>>([]) : accounts.OwnersAsync(accountId, ct);
}
