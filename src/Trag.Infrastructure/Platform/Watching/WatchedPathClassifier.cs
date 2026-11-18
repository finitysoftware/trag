using Trag.Infrastructure.Git.State;
using Trag.Domain.Paths;

namespace Trag.Infrastructure.Platform.Watching;

// Which changed paths the status has to be re-read for. A file or a folder, created, changed, removed or renamed, can be
// re-read on its own, Git's read of a folder's name covering what lies under it; anything that can change what other
// files mean — the ignore rules, a repository's own .git — needs the whole status.
internal static class WatchedPathClassifier
{
    public static WatchDecision WorkingPath(string root, string fullPath, WatcherChangeTypes kind)
    {
        var path = Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        if (path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return WatchDecision.Ignore;
        // A submodule's git directory can sit inside the working tree, where only this watcher sees its shared index and
        // its monitor's cookies.
        var segments = path.Split('/');
        if (segments[..^1].Contains(".git", StringComparer.OrdinalIgnoreCase) && (IsSharedIndex(segments) || IsMonitorFolder(segments)))
            return WatchDecision.Ignore;
        var name = Path.GetFileName(path);
        if (name.Equals(".gitignore", LocalPaths.Comparison) || name.Equals(".gitattributes", LocalPaths.Comparison) || name.Equals(".git", LocalPaths.Comparison))
            return WatchDecision.Reconcile;
        // Windows changes a folder's time whenever an entry in it changes, and the entry reports itself.
        if (kind == WatcherChangeTypes.Changed && Directory.Exists(fullPath)) return WatchDecision.Ignore;
        return WatchDecision.Signal(OutermostRepository(root, path) ?? path);
    }

    // The superproject knows a submodule only as one entry, by the submodule's path, so a file inside one is that entry's
    // change; a submodule's own submodules are inside it too.
    private static string? OutermostRepository(string root, string path)
    {
        var segments = path.Split('/');
        for (var count = 1; count < segments.Length; count++)
        {
            var folder = string.Join('/', segments[..count]);
            if (Path.Exists(Path.Combine(root, folder, ".git"))) return folder;
        }
        return null;
    }

    public static WatchDecision GitPath(string directory, string fullPath)
    {
        var path = Path.GetRelativePath(directory, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        if (path.StartsWith(IndexTransaction.NamePrefix, StringComparison.Ordinal)) return WatchDecision.Ignore;
        var segments = path.Split('/');
        // A linked worktree or a submodule keeps its own shared index in its git directory here.
        if (IsSharedIndex(segments)) return WatchDecision.Ignore;
        // A linked worktree's and a submodule's monitor keep their cookies in their git directory here.
        if (IsMonitorFolder(segments)) return WatchDecision.Ignore;
        // Object writes, reflogs and temporary locks do not change the displayed status.
        if (path.StartsWith("objects/", StringComparison.Ordinal) || path.StartsWith("logs/", StringComparison.Ordinal)
            || path.EndsWith(".lock", StringComparison.Ordinal)) return WatchDecision.Ignore;
        return WatchDecision.Reconcile;
    }

    // Git freshens a split index's shared file on every read of the index, Trag's own included, and writes a new one only
    // beside a new index; a ref may carry the same name.
    private static bool IsSharedIndex(string[] segments) =>
        segments[^1].StartsWith("sharedindex", StringComparison.Ordinal) && !segments.Contains("refs");

    // Git's file-system monitor creates and deletes a cookie in its own folder for every query, Trag's own reads included;
    // a ref may carry the folder's name.
    private static bool IsMonitorFolder(string[] segments) =>
        segments.Contains("fsmonitor--daemon") && !segments.Contains("refs");
}
