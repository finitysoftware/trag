namespace Trag.Infrastructure.Git.Processes;

// What every Git call on a local repository decides before its own arguments begin. The strings live here so that a new
// runner cannot quietly forget the one that matters most: an empty GIT_ALLOW_PROTOCOL, which keeps a read from reaching
// a remote. It allows no protocol whatever the user's protocol.allow and protocol.<name>.allow say, and Git before
// 2.45, which knows no GIT_NO_LAZY_FETCH, would otherwise fetch a version a partial clone lacks from a remote whose
// protocol the user allows by name. From 2.45 on, GIT_NO_LAZY_FETCH keeps such a read from starting that fetch at all.
// A preset is named after what it refuses, and the two below are the only combinations Trag uses.
internal sealed class LocalGitPreset
{
    private readonly string[] configuration;

    private LocalGitPreset(bool fileMonitor)
    {
        configuration = fileMonitor ? [] : ["-c", "core.fsmonitor=false"];
    }

    // Reading the repository. Git's own file-system monitor may answer where the user configured one; the working tree's
    // status turns it off.
    public static readonly LocalGitPreset Reading = new(fileMonitor: true);

    // Writing, the HEAD read that verifies a write, and every read of commits, trees and blobs, which sees them as a write
    // will: through git replace, as Git does. The monitor is not asked, because a write must see the real state.
    public static readonly LocalGitPreset Writing = new(fileMonitor: false);

    // Shared, because no caller may change them.
    public IReadOnlyDictionary<string, string?> Variables { get; } = new Dictionary<string, string?> { ["GIT_NO_LAZY_FETCH"] = "1", ["GIT_ALLOW_PROTOCOL"] = "" };

    public string[] Arguments(string[] arguments) => [.. configuration, .. arguments];

}
