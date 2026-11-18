using Trag.Desktop.Composition;

namespace Trag.Desktop.UnitTests.Composition;

public sealed class AppPathsTests
{
    private static LaunchMode Mode(bool probe, string? settings = null) => new(LaunchKind.Ui, probe, probe, false, false, settings);

    [Fact]
    public void WhenTragLaunchesNormally_ShouldKeepEveryFileUnderTheLocalTragFolder()
    {
        var local = Path.Combine(Path.GetTempPath(), "trag-paths-tests");
        var paths = AppPaths.Resolve(Mode(probe: false), local);
        var folder = Path.Combine(local, "Trag");
        Assert.Equal(Path.Combine(folder, "settings.json"), paths.Settings);
        Assert.Equal(Path.Combine(folder, "accounts.json"), paths.Accounts);
        Assert.Equal(Path.Combine(folder, "repositories.json"), paths.Catalog);
        Assert.Equal(Path.Combine(folder, "settings.json.diff.json"), paths.Layout);
        Assert.Equal(Path.Combine(folder, "crash-logs"), paths.CrashLogs);
    }

    [Fact]
    public void WhenAProbeLaunchNamesASettingsFile_ShouldUseItAndPutTheOtherFilesBesideIt()
    {
        var settings = Path.Combine(Path.GetTempPath(), "trag-probe-tests", "settings.json");
        var paths = AppPaths.Resolve(Mode(probe: true, settings), "unused");
        Assert.Equal(settings, paths.Settings);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(settings)!, "accounts.json"), paths.Accounts);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(settings)!, "repositories.json"), paths.Catalog);
        Assert.Equal(settings + ".diff.json", paths.Layout);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(settings)!, "crash-logs"), paths.CrashLogs);
    }

    // An empty TRAG_PROBE_SETTINGS (possible outside Windows) names no file either.
    [Fact]
    public void WhenAProbeLaunchNamesAnEmptySettingsFile_ShouldRememberNothing()
    {
        var paths = AppPaths.Resolve(Mode(probe: true, ""), "unused");
        Assert.Null(paths.Settings); Assert.Null(paths.Accounts); Assert.Null(paths.Catalog); Assert.Null(paths.Layout); Assert.Null(paths.CrashLogs);
    }

    // Outside Windows the folder is empty when neither XDG_DATA_HOME nor a home folder is known, and a folder that is not
    // a full path would put Trag's files wherever it was started, possibly inside a repository: Trag remembers nothing.
    [Theory]
    [InlineData("")]
    [InlineData("data")]
    public void WhenLocalApplicationDataIsEmpty_ShouldNotUseARelativePath(string localApplicationData)
    {
        var paths = AppPaths.Resolve(Mode(probe: false), localApplicationData);
        Assert.Null(paths.Settings); Assert.Null(paths.Accounts); Assert.Null(paths.Catalog); Assert.Null(paths.Git); Assert.Null(paths.Layout); Assert.Null(paths.CrashLogs);
    }

    [Fact]
    public void WhenAProbeLaunchNamesNoSettingsFile_ShouldRememberNothing()
    {
        var paths = AppPaths.Resolve(Mode(probe: true), "unused");
        Assert.Null(paths.Settings); Assert.Null(paths.Accounts); Assert.Null(paths.Catalog); Assert.Null(paths.Layout); Assert.Null(paths.CrashLogs);
    }
}
