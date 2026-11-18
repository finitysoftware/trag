namespace Trag.Infrastructure.Platform.Watching;

// Where Git reads the user's own configuration (git help config, FILES): GIT_CONFIG_GLOBAL when it is set, otherwise
// $XDG_CONFIG_HOME/git/config (~/.config/git/config by default) and ~/.gitconfig. Git for Windows takes HOME; without
// it the folder HOMEDRIVE and HOMEPATH name together, when that folder is there (a home share may be disconnected), and
// USERPROFILE otherwise.
internal static class GlobalConfigFiles
{
    public static IReadOnlyList<string> Of(Func<string, string?> environment)
    {
        if (environment("GIT_CONFIG_GLOBAL") is { Length: > 0 } global) return [global];
        if ((environment("HOME") ?? HomeShare(environment) ?? environment("USERPROFILE")) is not { Length: > 0 } home) return [];
        var xdg = environment("XDG_CONFIG_HOME") is { Length: > 0 } configured ? configured : Path.Combine(home, ".config");
        return [Path.Combine(xdg, "git", "config"), Path.Combine(home, ".gitconfig")];
    }

    private static string? HomeShare(Func<string, string?> environment) =>
        environment("HOMEDRIVE") + environment("HOMEPATH") is var share && Directory.Exists(share) ? share : null;
}
