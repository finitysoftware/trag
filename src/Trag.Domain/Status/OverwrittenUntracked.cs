using Trag.Domain.Paths;

namespace Trag.Domain.Status;

// The untracked files Git writes over without asking when it puts paths of a commit in place, as a hard reset does: an
// untracked file on one of those paths, one where a path goes into a folder, and one in a folder where a path is a file.
public static class OverwrittenUntracked
{
    public static IReadOnlyList<GitPath> Of(IEnumerable<FileStatus> files, IReadOnlyList<GitPath> written)
    {
        var paths = written.Select(path => path.Key).ToHashSet();
        var folders = written.SelectMany(path => Folders(path.Bytes)).ToHashSet();
        return files.Where(file => file.IsUntracked
                && (paths.Contains(file.Path.Key) || folders.Contains(file.Path.Key) || Folders(file.Path.Bytes).Any(paths.Contains)))
            .Select(file => file.Path).ToList();
    }

    // The keys of the folders a path lies in: "a/b/c" lies in "a" and in "a/b".
    private static IEnumerable<string> Folders(byte[] path)
    {
        for (var i = 0; i < path.Length; i++)
            if (path[i] == (byte)'/') yield return Convert.ToHexString(path, 0, i);
    }
}
