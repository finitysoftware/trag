using System.Text;
using Trag.Domain.Paths;

namespace Trag.Domain.Status;

// What a new snapshot is known to have changed. The watcher names the working files it saw change; any other read (an
// open, a refresh, a write, a change under .git, the polling fallback, an index or HEAD that moved meanwhile) may have
// changed anything.
public sealed class SnapshotChange
{
    private readonly HashSet<string>? keys;

    private SnapshotChange(HashSet<string>? keys) => this.keys = keys;

    public static readonly SnapshotChange Everything = new(null);

    // The watcher's paths are repository-relative text; a path is known by its bytes, as Git names it.
    public static SnapshotChange Of(IEnumerable<string> paths) => new(paths.Select(Key).ToHashSet(StringComparer.Ordinal));

    // The paths hold only when the new snapshot differs from the previous one in its files alone.
    public static SnapshotChange Between(RepositorySnapshot previous, RepositorySnapshot next, IEnumerable<string>? paths) =>
        paths is null || !SnapshotComparison.SameStateForSync(previous, next) ? Everything : Of(paths);

    public bool IsEverything => keys is null;

    // Whether the diff shown for a file has to be read again: its file may have changed, or its status did. Status codes
    // can stay MM while the content changes, so an unchanged status is no proof on its own.
    public bool Rereads(FileStatus shown, FileStatus now) => keys is null || !shown.HasSameChange(now) || Touches(shown) || Touches(now);

    private bool Touches(FileStatus file) => file.Paths.Any(Touches);

    // Whether a file named by its repository-relative path may have changed.
    public bool Touches(string path) => keys is null || Touches(new GitPath(Encoding.UTF8.GetBytes(path)));

    // A folder the watcher names, moved or removed, holds every file under it.
    private bool Touches(GitPath path) => keys!.Contains(path.Key)
        || path.Bytes.Index().Any(item => item.Item == (byte)'/' && keys.Contains(Convert.ToHexString(path.Bytes, 0, item.Index)));

    private static string Key(string path) => new GitPath(Encoding.UTF8.GetBytes(path)).Key;
}
