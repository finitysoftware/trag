namespace Trag.Desktop.Composition;

// Where the app keeps its files: %LOCALAPPDATA%/Trag/settings.json with accounts.json, repositories.json, git.json and
// the crash-logs folder beside it, and settings.json.diff.json for the layout. A probe launch takes TRAG_PROBE_SETTINGS
// instead, or no files at all.
public sealed class AppPaths
{
    public string? Settings { get; }
    public string? Accounts { get; }
    public string? Catalog { get; }
    public string? Git { get; }
    public string? Layout { get; }
    public string? CrashLogs { get; }

    private AppPaths(string? settings)
    {
        Settings = settings;
        Accounts = settings is null ? null : Sibling(settings, "accounts.json");
        Catalog = settings is null ? null : Sibling(settings, "repositories.json");
        Git = settings is null ? null : Sibling(settings, "git.json");
        Layout = settings is null ? null : settings + ".diff.json";
        CrashLogs = settings is null ? null : Sibling(settings, "crash-logs");
    }

    // An empty probe setting names no file either: the probe remembers nothing. Nor does a launch whose local application
    // data is no full path, which would put the files wherever Trag was started.
    public static AppPaths Resolve(LaunchMode mode, string localApplicationData) =>
        new((mode.IsProbeMode ? mode.ProbeSettingsPath
            : Path.IsPathFullyQualified(localApplicationData) ? Path.Combine(localApplicationData, "Trag", "settings.json") : null) is { Length: > 0 } settings ? settings : null);

    private static string Sibling(string settings, string name) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings))!, name);
}
