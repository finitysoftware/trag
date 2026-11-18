using Trag.Domain.Catalog;
using Trag.Domain.Naming;
using Trag.Domain.Paths;

namespace Trag.Infrastructure.Platform.Storage;

// What the catalog keeps from a list of links, whether it came from disk or from the shell: a usable path, a name to
// show, and no more entries than the list is allowed to hold. Two paths are one link exactly when the catalog's list
// finds them as one, so what is saved never holds two entries the list merged, nor merges two it keeps apart.
internal static class RepositoryEntrySanitizer
{
    // A null in a list read from disk, which only a hand edit leaves, is dropped as an entry without a path is.
    public static RepositoryEntry[] Sanitize(IEnumerable<RepositoryEntry> entries) => entries
        .Where(entry => entry is not null && PlainNames.IsValid(entry.Path, 4096))
        .Select(entry => entry with { Name = string.IsNullOrWhiteSpace(entry.Name) ? RepositoryEntry.DisplayName(entry.Path) : entry.Name.Trim(),
            RemoteUrl = string.IsNullOrWhiteSpace(entry.RemoteUrl) || entry.RemoteUrl.Length > GitLimits.AddressLength ? null : entry.RemoteUrl,
            Branch = string.IsNullOrWhiteSpace(entry.Branch) || entry.Branch.Length > 512 ? null : entry.Branch })
        .DistinctBy(entry => LocalPaths.Key(entry.Path), LocalPaths.Comparer)
        .Take(RepositoryEntry.MaximumListed).ToArray();
}
