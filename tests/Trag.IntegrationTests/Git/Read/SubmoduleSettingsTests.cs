using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Submodules Git's own update leaves as they are: one that is not active, by its own setting or by submodule.active's
// pathspecs, and one whose update is not a checkout. None of them is offered for an update, so neither the notice nor a
// finished update waits for them.
public sealed class SubmoduleSettingsTests
{
    private static async Task<string> CloneAsync(GitFixture f)
    {
        var clone = Path.Combine(f.Container, "clone");
        await f.GitAsync("-c", "protocol.file.allow=always", "clone", "--", f.Root, clone);
        await SubmoduleFixture.IdentifyAsync(f, clone);
        return clone;
    }

    [Theory]
    [InlineData("submodule.sub.active", "false")]
    [InlineData("submodule.active", "other")]
    [InlineData("submodule.sub.update", "none")]
    public async Task WhenGitWouldLeaveAMissingSubmodule_ShouldNotListIt(string key, string value)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);
        await f.GitAtAsync(clone, "config", key, value);

        Assert.Empty((await f.ReadAsync(clone)).SubmodulesToUpdate);
    }

    [Theory]
    [InlineData("submodule.sub.active", "false")]
    [InlineData("submodule.sub.update", "rebase")]
    [InlineData("submodule.sub.update", "none")]
    public async Task WhenGitWouldLeaveAMovedSubmodule_ShouldNotListIt(string key, string value)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "commit", "--allow-empty", "-m", "Sub two");
        await f.GitAsync("config", key, value);

        Assert.Empty((await f.ReadAsync()).SubmodulesToUpdate);
    }

    // submodule.active's pathspecs are Git's: a pattern that matches the submodule keeps it, and the submodule's own
    // setting goes before them.
    [Theory]
    [InlineData("s*", null)]
    [InlineData("other", "true")]
    public async Task WhenGitWouldUpdateAMissingSubmodule_ShouldListIt(string active, string? own)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);
        await f.GitAtAsync(clone, "config", "submodule.active", active);
        if (own is not null) await f.GitAtAsync(clone, "config", "submodule.sub.active", own);

        Assert.Equal(["sub"], (await f.ReadAsync(clone)).SubmodulesToUpdate);
    }

    // Deinit drops the submodule's section from the configuration and keeps its repository under .git/modules, which a
    // submodule never checked out has not got. The user chose to be without it, so it is left out even where
    // submodule.active, as a recursive clone sets it, still matches its path.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenASubmoduleWasDeinitialized_ShouldNotOfferToUpdateIt(bool recursive)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = Path.Combine(f.Container, "clone");
        if (recursive) await f.GitAsync("-c", "protocol.file.allow=always", "clone", "--recurse-submodules", "--", f.Root, clone);
        else
        {
            await f.GitAsync("-c", "protocol.file.allow=always", "clone", "--", f.Root, clone);
            await f.GitAtAsync(clone, "-c", "protocol.file.allow=always", "submodule", "update", "--init");
        }
        await f.GitAtAsync(clone, "submodule", "deinit", "--", "sub");

        Assert.Empty((await f.ReadAsync(clone)).SubmodulesToUpdate);
    }

    // .gitmodules says how a submodule updates until the repository's own configuration says otherwise.
    [Fact]
    public async Task WhenGitmodulesSaysHowTheSubmoduleUpdates_ShouldFollowItUntilTheRepositorysOwnSettingDiffers()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);
        await f.GitAtAsync(clone, "config", "--file", ".gitmodules", "submodule.sub.update", "none");
        var fromGitmodules = (await f.ReadAsync(clone)).SubmodulesToUpdate;
        await f.GitAtAsync(clone, "config", "submodule.sub.update", "checkout");

        var own = (await f.ReadAsync(clone)).SubmodulesToUpdate;

        Assert.Empty(fromGitmodules);
        Assert.Equal(["sub"], own);
    }
}
