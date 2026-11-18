using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Paths;

namespace Trag.Application.Catalog;

// The persisted list of repositories added to Trag, matched by normalized path: read once per session, saved after every
// change. A save makes the changes since the last one again, to the list as it is stored then, so what another instance
// of Trag saved meanwhile stays; changes made while a save runs wait for the next one.
public sealed class RepositoryCatalogList(IRepositoryCatalogStore? store)
{
    private readonly List<RepositoryEntry> entries = [];
    private readonly List<Action<List<RepositoryEntry>>> unsaved = [];
    public bool Loaded { get; private set; }
    public bool IsFull => entries.Count >= RepositoryEntry.MaximumListed;
    public IEnumerable<RepositoryEntry> Ordered => RepositoryEntry.InDisplayOrder(entries);

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (store is null || Loaded)
            return;
        var loaded = await store.LoadAsync(ct);
        ct.ThrowIfCancellationRequested();
        entries.Clear();
        entries.AddRange(loaded);
        Loaded = true;
    }
    public async Task SaveAsync(CancellationToken ct)
    {
        if (store is null)
            return;
        var changes = unsaved.ToArray();
        var saved = await store.UpdateAsync(stored => Changed(stored, changes), ct);
        unsaved.RemoveRange(0, changes.Length);
        var shown = Changed(saved, unsaved);
        entries.Clear();
        entries.AddRange(shown);
    }
    public int IndexOf(string path) => IndexOf(entries, path);
    public RepositoryEntry? Find(string path) => IndexOf(path) is >= 0 and var index ? entries[index] : null;
    public void AddOrReplace(RepositoryEntry entry) => Change(list =>
    {
        var index = IndexOf(list, entry.Path);
        if (index < 0) list.Add(entry);
        else list[index] = entry;
    });
    public void Remove(string path) => Change(list => list.RemoveAll(entry => LocalPaths.Same(entry.Path, path)));
    public void Replace(RepositoryEntry entry) => Change(list =>
    {
        var index = IndexOf(list, entry.Path);
        if (index >= 0)
            list[index] = entry;
    });

    private void Change(Action<List<RepositoryEntry>> change)
    {
        change(entries);
        unsaved.Add(change);
    }

    private static List<RepositoryEntry> Changed(IEnumerable<RepositoryEntry> stored, IEnumerable<Action<List<RepositoryEntry>>> changes)
    {
        var list = stored.ToList();
        foreach (var change in changes)
            change(list);
        return list;
    }

    private static int IndexOf(List<RepositoryEntry> list, string path) => list.FindIndex(entry => LocalPaths.Same(entry.Path, path));
}
