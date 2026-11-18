using Trag.TestSupport;

namespace Trag.IntegrationTests.Git;

// The rows of the feature × name matrix: labels of TestNames.Hostile, so a newline or a 4,000-byte
// path never reaches a test's name. The paths Windows cannot hold are no rows here.
public static class HostileNameRows
{
    // Every path Git tracks on this system.
    public static TheoryData<string> Tracked => Labels(path => path.Here == TestNames.PathFate.Tracked);

    // Every path this system holds as a file, the names Git for Windows cannot index included: Git lists them as untracked.
    public static TheoryData<string> Untracked => Labels(path => path.Here != TestNames.PathFate.NotHere);

    // The names Git for Windows refuses to index, the same on every system so the data is never empty; a test returns
    // off Windows.
    public static TheoryData<string> Unindexable => Labels(path => path.OnWindows == TestNames.PathFate.RefusedByGit);

    // Folders a repository can live in on this system. Elsewhere a folder of 4,000 bytes leaves Git no room under
    // PATH_MAX (4,096) for the files it keeps in it.
    public static TheoryData<string> UsableFolders => Labels(path => path.Here == TestNames.PathFate.Tracked
        && !(OperatingSystem.IsWindows() ? PastMaxPath(path) : path.Path.Length >= 4000));

    // Folders Git for Windows cannot work in: a reserved device name, and a folder past MAX_PATH, which Git cannot make
    // its working directory, long paths or not. The same on every system so the data is never empty; a test returns off
    // Windows.
    public static TheoryData<string> UnusableFolders => Labels(path => path.OnWindows == TestNames.PathFate.RefusedByGit
        || path.OnWindows == TestNames.PathFate.Tracked && PastMaxPath(path));

    // Under any fixture's folder, a path of 255 characters ends past MAX_PATH (260).
    private static bool PastMaxPath(TestNames.HostilePath path) => path.Path.Length >= 255;

    private static TheoryData<string> Labels(Func<TestNames.HostilePath, bool> row) => new(TestNames.Hostile.Where(row).Select(path => path.Label));
}
