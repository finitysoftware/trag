using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Status;

namespace Trag.Application.Catalog;

// The local side of the repository catalog without a screen: the list, adding a folder once it proves to be a working
// copy, initializing one there first, recording an opened repository, checking every link, and removal. The outcomes are
// records, a cancellation or a failure included, so a caller never catches; what they mean to the user is said by
// Presentation.Text. Continuations stay on the caller's context, because the callers update observable rows.
public sealed class LocalRepositoryCatalog(IRepositoryCatalogStore? store, IRepositoryProber? prober, IRepositoryCreator? creator)
{
    public RepositoryCatalogList Entries { get; } = new(store);
    public TimeProvider Time { get; set; } = TimeProvider.System;

    public Task EnsureLoadedAsync(CancellationToken ct) => Entries.EnsureLoadedAsync(ct);

    // A save that fails leaves the change in this session only; the caller says so.
    public async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try { await Entries.SaveAsync(ct); return true; }
        catch (Exception) { return false; }
    }

    // The folder joins the list when it is a working copy; its root is what is recorded, so a subfolder finds its repository.
    public async Task<CatalogAddition> AddPathAsync(string path, CancellationToken ct)
    {
        if (prober is null) throw new InvalidOperationException("This catalog cannot check folders.");
        return await Settle(() => AddProbedPathAsync(prober, path, ct));
    }

    private async Task<CatalogAddition> AddProbedPathAsync(IRepositoryProber prober, string path, CancellationToken ct)
    {
        await Entries.EnsureLoadedAsync(ct);
        var probe = await prober.ProbeAsync(path, ct);
        if (probe.Status == RepositoryLinkStatus.Missing) return new(CatalogAdditionOutcome.Missing, null);
        if (probe.Status == RepositoryLinkStatus.Untrusted) return new(CatalogAdditionOutcome.Untrusted, null);
        if (probe.Status == RepositoryLinkStatus.NeedsNewerGit) return new(CatalogAdditionOutcome.NeedsNewerGit, null);
        if (probe.Status == RepositoryLinkStatus.Bare) return new(CatalogAdditionOutcome.Bare, null);
        if (probe.Status == RepositoryLinkStatus.GitDirectory) return new(CatalogAdditionOutcome.GitDirectory, null);
        if (probe.Status != RepositoryLinkStatus.Valid) return new(CatalogAdditionOutcome.NotRepository, null);
        var root = probe.Root ?? path;
        var known = Entries.Find(root);
        return await ListAsync(known, known is not null
            ? known with { Branch = probe.Branch, RemoteUrl = probe.RemoteUrl }
            : new RepositoryEntry(root, RepositoryEntry.DisplayName(root), Time.GetUtcNow(), null, probe.RemoteUrl, probe.Branch), ct);
    }

    // Git initializes the folder, then it is added like any other.
    public async Task<CatalogAddition> InitializeAsync(string path, CancellationToken ct)
    {
        if (creator is null) throw new InvalidOperationException("This catalog cannot initialize repositories.");
        var initialized = await Settle(async () => { await creator.InitializeAsync(path, ct); return new CatalogAddition(CatalogAdditionOutcome.Added, null); });
        return initialized.Outcome == CatalogAdditionOutcome.Added ? await AddPathAsync(path, ct) : initialized;
    }

    // An opened repository moves to the top of the list with its current branch and origin.
    public Task<CatalogAddition> RecordOpenedAsync(RepositorySnapshot snapshot, CancellationToken ct) => Settle(() => RecordAsync(snapshot, ct));

    private async Task<CatalogAddition> RecordAsync(RepositorySnapshot snapshot, CancellationToken ct)
    {
        await Entries.EnsureLoadedAsync(ct);
        var origin = snapshot.Origin?.FetchAddress;
        var now = Time.GetUtcNow();
        var known = Entries.Find(snapshot.Root);
        return await ListAsync(known, known is not null
            ? known with { LastOpenedAt = now, Branch = snapshot.Branch, RemoteUrl = origin ?? known.RemoteUrl }
            : new RepositoryEntry(snapshot.Root, RepositoryEntry.DisplayName(snapshot.Root), now, now, origin, snapshot.Branch), ct);
    }

    // The entry replaces the one known at its root, or joins the list unless it is full; the list is saved.
    private async Task<CatalogAddition> ListAsync(RepositoryEntry? known, RepositoryEntry entry, CancellationToken ct)
    {
        if (known is null && Entries.IsFull) return new(CatalogAdditionOutcome.Full, null);
        Entries.AddOrReplace(entry);
        await Entries.SaveAsync(ct);
        return new(known is null ? CatalogAdditionOutcome.Added : CatalogAdditionOutcome.AlreadyListed, entry);
    }

    // One link; a check that fails is reported as unknown rather than thrown.
    private async Task<RepositoryProbe> ProbeLinkAsync(string path, CancellationToken ct)
    {
        if (prober is null) return new(RepositoryLinkStatus.Unknown);
        try { return await prober.ProbeAsync(path, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(RepositoryLinkStatus.Unknown); }
    }

    // Every link, four at a time, telling each item while it is being checked. An item whose check changed its entry
    // hands the entry back; the list is saved once at the end, while the read is still the current one.
    public async Task ProbeAllAsync<T>(IReadOnlyList<T> items, Func<T, string> pathOf, Action<T, bool> checking,
        Func<T, RepositoryProbe, RepositoryEntry?> apply, Func<bool> current, CancellationToken ct)
    {
        if (prober is null || items.Count == 0)
            return;
        using var slots = new SemaphoreSlim(4);
        var changes = await Task.WhenAll(items.Select(async item =>
        {
            await slots.WaitAsync(ct);
            checking(item, true);
            try
            {
                var probe = await ProbeLinkAsync(pathOf(item), ct);
                if (!current() || ct.IsCancellationRequested) return false;
                if (apply(item, probe) is not { } changed) return false;
                Entries.Replace(changed);
                return true;
            }
            finally
            {
                checking(item, false);
                slots.Release();
            }
        }));
        if (changes.Any(changed => changed) && current())
            await Entries.SaveAsync(ct);
    }

    public void Remove(string path) => Entries.Remove(path);

    // A step that is cancelled or fails is an outcome like any other.
    private static async Task<CatalogAddition> Settle(Func<Task<CatalogAddition>> step)
    {
        try { return await step(); }
        catch (OperationCanceledException) { return new(CatalogAdditionOutcome.Canceled, null); }
        catch (Exception) { return new(CatalogAdditionOutcome.Failed, null); }
    }
}
