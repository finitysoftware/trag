using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Paths;

namespace Trag.Application.Catalog;

// The local side of the catalog: the list's entries as items with their links, and what changes them — a rebuild after
// the list changed, the link check, adding, initializing and removing a folder, and recording an opened repository. The
// outcomes of adding are the catalog's records, which CatalogModel puts in the picker's words. IsInitializing tells the
// picker that Git is making a folder a repository, Items that the items were replaced, Checked that a link check answered.
public sealed partial class LocalRepositoriesModel(LocalRepositoryCatalog catalog, StatusBarState statusBar) : ObservableObject
{
    private RepositoryCatalogList Entries => catalog.Entries;
    // The clock the list's times are told by.
    public TimeProvider Time => catalog.Time;
    public IReadOnlyList<LocalRepository> Items { get; private set; } = [];
    [ObservableProperty] public partial bool IsInitializing { get; private set; }
    public event Action? Checked;

    public Task EnsureLoadedAsync(CancellationToken ct) => catalog.EnsureLoadedAsync(ct);

    // Items keep their identity across rebuilds; a path the caller just verified is a valid link without a check.
    public void Rebuild(string? verifiedPath = null)
    {
        var verifiedKey = verifiedPath is null ? null : LocalPaths.Key(verifiedPath);
        var existing = new Dictionary<string, LocalRepository>(LocalPaths.Comparer);
        foreach (var item in Items)
            existing.TryAdd(LocalPaths.Key(item.Path), item);
        Items = Entries.Ordered.Select(entry =>
        {
            var key = LocalPaths.Key(entry.Path);
            if (!existing.TryGetValue(key, out var item)) item = new(entry);
            if (item.Entry != entry)
                item.Entry = entry;
            if (verifiedKey is not null && LocalPaths.Comparer.Equals(key, verifiedKey))
                item.Status = RepositoryLinkStatus.Valid;
            return item;
        }).ToArray();
        OnPropertyChanged(nameof(Items));
    }

    // Every link, four at a time, with the item saying it is being checked; a changed branch or origin is written back
    // once at the end.
    public Task ProbeAsync(Func<bool> current, CancellationToken ct) => catalog.ProbeAllAsync(Items, item => item.Path,
        (item, checking) => item.IsChecking = checking, (item, probe) =>
        {
            var changed = item.Apply(probe);
            Checked?.Invoke();
            return changed ? item.Entry : null;
        }, current, ct);

    public Task<CatalogAddition> AddAsync(string path) => catalog.AddPathAsync(path, CancellationToken.None);

    public async Task<CatalogAddition> InitializeAsync(string path)
    {
        IsInitializing = true;
        try { return await catalog.InitializeAsync(path, CancellationToken.None); }
        finally { IsInitializing = false; }
    }

    // The entry leaves the list at once; false when the list could not be saved, which does not bring it back.
    public Task<bool> RemoveAsync(LocalRepository item)
    {
        catalog.Remove(item.Path);
        Rebuild();
        return catalog.TrySaveAsync(CancellationToken.None);
    }

    // An opened repository moves to the top with its current branch and origin; a list that cannot record it, or is full
    // and does not hold it, says so in the status bar.
    public async Task RecordOpenedAsync(RepositorySnapshot snapshot, CancellationToken ct)
    {
        var recorded = await catalog.RecordOpenedAsync(snapshot, ct);
        if (recorded.Outcome == CatalogAdditionOutcome.Failed) statusBar.Notice = CatalogText.OpenedNotRecorded;
        else if (recorded.Outcome == CatalogAdditionOutcome.Full) statusBar.Notice = CatalogText.OpenedNotListed;
        else if (recorded.Entry is not null) Rebuild(snapshot.Root);
    }

    // The items by their origin, for the hosted repositories to find their clones.
    public RemoteRepositoryMatcher<LocalRepository> Clones() => new(Items, item => item.Entry.RemoteUrl);

    // The item of a folder the list took, rebuilt with its link known to be valid; null when the list took none.
    public LocalRepository? Accept(CatalogAddition added)
    {
        if (added.Entry is not { } entry) return null;
        Rebuild(entry.Path);
        return Items.First(candidate => LocalPaths.Same(candidate.Path, entry.Path));
    }
}
