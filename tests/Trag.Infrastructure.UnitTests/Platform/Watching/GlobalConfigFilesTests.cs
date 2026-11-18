using Trag.Infrastructure.Platform.Watching;

namespace Trag.Infrastructure.UnitTests.Platform.Watching;

// Where Git reads the user's own configuration (git help config, FILES).
public sealed class GlobalConfigFilesTests
{
    private static IReadOnlyList<string> Of(params (string Name, string Value)[] variables) =>
        GlobalConfigFiles.Of(name => variables.FirstOrDefault(variable => variable.Name == name).Value);

    [Fact]
    public void WhenGitConfigGlobalIsSet_ShouldWatchThatFileAlone() =>
        Assert.Equal([Path.Combine("x", "global")], Of(("GIT_CONFIG_GLOBAL", Path.Combine("x", "global")), ("HOME", "h")));

    [Fact]
    public void WhenOnlyTheHomeIsKnown_ShouldWatchTheXdgFileAndTheDotFile()
    {
        Assert.Equal([Path.Combine("h", ".config", "git", "config"), Path.Combine("h", ".gitconfig")], Of(("HOME", "h")));
        Assert.Equal([Path.Combine("p", ".config", "git", "config"), Path.Combine("p", ".gitconfig")], Of(("USERPROFILE", "p")));
        Assert.Equal([Path.Combine("x", "git", "config"), Path.Combine("h", ".gitconfig")], Of(("HOME", "h"), ("XDG_CONFIG_HOME", "x")));
    }

    // Without HOME, Git for Windows reads the configuration in the folder HOMEDRIVE and HOMEPATH name together, when that
    // folder is there (a home share may be disconnected), and in USERPROFILE otherwise.
    [Fact]
    public void WhenHomeIsUnset_ShouldWatchTheFolderHomeDriveAndHomePathNameWhenItIsThere()
    {
        var share = Directory.CreateTempSubdirectory("trag-home-").FullName;
        try
        {
            Assert.Equal([Path.Combine(share, ".config", "git", "config"), Path.Combine(share, ".gitconfig")],
                Of(("HOMEDRIVE", share[..2]), ("HOMEPATH", share[2..]), ("USERPROFILE", "p")));
            Assert.Equal([Path.Combine("p", ".config", "git", "config"), Path.Combine("p", ".gitconfig")],
                Of(("HOMEDRIVE", share[..2]), ("HOMEPATH", share[2..] + "-missing"), ("USERPROFILE", "p")));
        }
        finally { Directory.Delete(share); }
    }

    [Fact]
    public void WhenNoHomeIsKnown_ShouldWatchNothing() => Assert.Empty(Of());
}
