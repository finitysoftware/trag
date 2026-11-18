using Trag.Domain.Paths;

namespace Trag.Infrastructure.Git.State;

// A path that came from Git or from the user is only read after it is proven to stay inside the repository and to reach
// no reparse point. Both are pure checks: the caller decides what to do when they fail.
internal static class RepositoryPathGuard
{
    public static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                LocalPaths.Comparison)) throw new IOException("Path outside repository.");
        return full;
    }

    public static bool HasLink(string root, string path)
    {
        for (var part = path; part.Length > Path.TrimEndingDirectorySeparator(root).Length; part = Path.GetDirectoryName(part)!)
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
