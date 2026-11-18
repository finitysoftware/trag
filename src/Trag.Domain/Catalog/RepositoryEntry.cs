namespace Trag.Domain.Catalog;

public sealed record RepositoryEntry(string Path, string Name, DateTimeOffset AddedAt, DateTimeOffset? LastOpenedAt = null,
    string? RemoteUrl = null, string? Branch = null)
{
    // The most repositories the list holds, and the settings keep.
    public const int MaximumListed = 500;

    // The name a repository goes by: its folder's name, or the path itself for a drive root.
    public static string DisplayName(string path)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    // Most recently opened first, then most recently added, then by name.
    public static IEnumerable<RepositoryEntry> InDisplayOrder(IEnumerable<RepositoryEntry> entries) => entries
        .OrderByDescending(entry => entry.LastOpenedAt ?? DateTimeOffset.MinValue).ThenByDescending(entry => entry.AddedAt).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    // A search typed in the picker finds the entry by its name or its folder, ignoring case and the spaces around it.
    public bool Matches(string search)
    {
        var query = search.Trim();
        return Name.Contains(query, StringComparison.OrdinalIgnoreCase) || Path.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
