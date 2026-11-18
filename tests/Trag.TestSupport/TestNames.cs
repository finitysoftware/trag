using System.Text;

namespace Trag.TestSupport;

// The one corpus of hostile paths for the tests that run a feature over paths.
// A path is Git's, with '/' between folders, and a test takes an entry by its label, which keeps runner
// output readable. Off Windows Git tracks every entry. On Windows it refuses to index the reserved device names that
// Windows 11 writes as ordinary files, and the entries marked NotHere stay off Windows: a Windows file name cannot hold
// them, and the 4,000-byte path is sized to Linux's PATH_MAX.
public static class TestNames
{
    public enum PathFate { Tracked, RefusedByGit, NotHere }

    public sealed record HostilePath(string Label, string Path, PathFate OnWindows = PathFate.Tracked)
    {
        public PathFate Here => OperatingSystem.IsWindows() ? OnWindows : PathFate.Tracked;
    }

    private const string Accented = "č ć ž đ š.txt";

    public static IReadOnlyList<HostilePath> Hostile { get; } =
    [
        new("space", "a b.txt"),
        new("double-space", "a  b.txt"),
        new("leading-space", " lead.txt"),
        new("space-before-extension", "trail .txt"),
        new("brackets", "[x].txt"),
        new("leading-dash", "-n.txt"),
        new("option-name", "--help"),
        new("nfc", Accented.Normalize(NormalizationForm.FormC)),
        new("nfd", Accented.Normalize(NormalizationForm.FormD)),
        new("cjk", "日本語.md"),
        new("emoji", "emoji 😀.txt"),
        new("rtl", "עברית.txt"),
        // 255 bytes and 255 UTF-16 units at once: the most a file name holds on ext4 and on NTFS.
        new("name-255-bytes", new string('n', 251) + ".txt"),
        // About 300 characters on its own, so the file lies past MAX_PATH from any root.
        new("deep-past-260", string.Concat(Enumerable.Repeat("folder-past-max-path-260/", 12)) + "deep.txt"),
        new("reserved-con", "CON.txt", PathFate.RefusedByGit),
        new("reserved-aux", "aux.c", PathFate.RefusedByGit),
        new("trailing-dot", "trail.txt.", PathFate.NotHere),
        new("trailing-space", "trail.txt ", PathFate.NotHere),
        new("star", "*.txt", PathFate.NotHere),
        new("question-mark", "?.txt", PathFate.NotHere),
        new("pathspec-magic", ":(top)x", PathFate.NotHere),
        new("backslash", "a\\b.txt", PathFate.NotHere),
        new("quotes", "\"quoted\".txt", PathFate.NotHere),
        new("newline", "new\nline.txt", PathFate.NotHere),
        // 4,000 bytes in names of at most 255, under Linux's PATH_MAX of 4,096 with the fixture's root.
        new("path-4000-bytes", string.Concat(Enumerable.Repeat(new string('d', 240) + "/", 16)) + new string('f', 140) + ".txt", PathFate.NotHere)
    ];

    // Two names Git tracks apart and a file system that ignores case keeps as one file.
    public static (string First, string Second) CaseTwins { get; } = ("Readme.txt", "README.txt");

    public static HostilePath Find(string label) => Hostile.Single(path => path.Label == label);
}
