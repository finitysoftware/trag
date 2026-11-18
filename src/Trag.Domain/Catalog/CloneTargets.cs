using Trag.Domain.Naming;

namespace Trag.Domain.Catalog;

// Where a clone is proposed to go: the folder the most recently opened repository sits in, or that folder's parent when
// the repository already sits under a folder named after its owner, then the owner and the name from the clone address,
// each made safe as a folder name. Without a repository yet, a Repos folder in the user's home folder.
public static class CloneTargets
{
    public static string Suggest(string cloneUrl, string fallbackOwner, string fallbackName, IEnumerable<RepositoryEntry> recentFirst, string home)
    {
        var owner = RemoteUrls.Owner(cloneUrl) ?? fallbackOwner;
        var name = RemoteUrls.Name(cloneUrl) ?? fallbackName;
        return Path.Combine(Root(recentFirst, home), Safe(owner), Safe(name));
    }

    // The clone under a folder the user picked, named after the repository.
    public static string Under(string folder, string cloneUrl, string fallbackName) => Path.Combine(folder, Safe(RemoteUrls.Name(cloneUrl) ?? fallbackName));

    public static string Root(IEnumerable<RepositoryEntry> recentFirst, string home)
    {
        var recent = recentFirst.FirstOrDefault();
        if (recent is null || Path.GetDirectoryName(recent.Path) is not { Length: > 0 } parent)
            return Path.Combine(home, "Repos");

        var owner = RemoteUrls.Owner(recent.RemoteUrl);
        if (owner is not null && string.Equals(Path.GetFileName(parent), owner, StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(parent) is { Length: > 0 } ownerRoot ? ownerRoot : parent;

        return parent;
    }

    // A folder name the file system accepts: invalid characters become '-', and an empty name becomes "repository".
    public static string Safe(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(segment.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return clean.Length == 0 ? "repository" : clean;
    }
}
