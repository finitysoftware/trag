using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Submodules not at the commits the branch records: one moved in its own working tree, one never checked out after a
// clone. Updating checks the recorded commits out and clones the missing ones, over the transports Git allows a
// submodule; a move that is staged is the user's.
public sealed class SubmoduleUpdateTests
{
    // Updating submodules is a transfer, so it goes through the synchronizer.
    private static GitWriteRouter Writer(GitFixture f) => new(f.Runner, new GitSyncFacade(f.Runner, new CliRepositoryReader(f.Runner)));

    private static async Task<string> CloneAsync(GitFixture f)
    {
        var clone = Path.Combine(f.Container, "clone");
        await f.GitAsync("-c", "protocol.file.allow=always", "clone", "--", f.Root, clone);
        await SubmoduleFixture.IdentifyAsync(f, clone);
        return clone;
    }

    [Fact]
    public async Task WhenSubmodulesAreMovedOrNotCheckedOut_ShouldListThemButNotAStagedMove()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "commit", "--allow-empty", "-m", "Sub two");

        var moved = (await f.ReadAsync()).SubmodulesToUpdate;
        var missing = (await f.ReadAsync(clone)).SubmodulesToUpdate;
        await f.GitAsync("add", "sub");
        var staged = (await f.ReadAsync()).SubmodulesToUpdate;

        Assert.Equal(["sub"], moved);
        Assert.Equal(["sub"], missing);
        Assert.Empty(staged);
    }

    // .gitmodules can still name a submodule the index no longer has, after one was taken out by hand or turned into a
    // folder of files; the update has nothing to check out for either.
    [Fact]
    public async Task WhenGitmodulesNamesASubmoduleTheIndexDoesNotHave_ShouldNotListIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.WriteAsync(Path.Combine("folder", "f.txt"), "f\n");
        await f.CommitAsync("Folder", "folder/f.txt");
        foreach (var name in new[] { "gone", "folder" })
        {
            await f.GitAsync("config", "--file", ".gitmodules", "submodule." + name + ".path", name);
            await f.GitAsync("config", "--file", ".gitmodules", "submodule." + name + ".url", "https://example.invalid/" + name + ".git");
        }

        Assert.Empty((await f.ReadAsync()).SubmodulesToUpdate);
    }

    [Fact]
    public async Task WhenSubmodulesAreUpdated_ShouldCheckOutTheRecordedCommitAndCloneTheMissingOne()
    {
        var (f, server) = await SubmoduleFixture.CreateServedAsync();
        using var _ = server;
        using var __ = f;
        var clone = await CloneAsync(f);
        var sub = Path.Combine(f.Root, "sub");
        var recorded = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var writer = Writer(f);

        var moved = await writer.ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync()), f.Token);
        var missing = await writer.ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync(clone)), f.Token);

        Assert.True(moved.Outcome == GitWriteOutcome.Succeeded, moved.Message + moved.Details);
        Assert.Equal((SubmoduleText.Updated, recorded), (moved.Message, await f.GitAtAsync(sub, "rev-parse", "HEAD")));
        Assert.True(missing.Outcome == GitWriteOutcome.Succeeded, missing.Message + missing.Details);
        Assert.Equal(recorded, await f.GitAtAsync(Path.Combine(clone, "sub"), "rev-parse", "HEAD"));
        Assert.Empty((await f.ReadAsync(clone)).SubmodulesToUpdate);
    }

    // Many submodules never checked out make a command line longer than Windows allows; they are all listed still.
    [Fact]
    public async Task WhenAThousandSubmodulesAreNotCheckedOut_ShouldListThemAll()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        string[] paths = [.. Enumerable.Range(0, 1000).Select(i => $"modules/a-submodule-with-a-rather-long-name-{i:0000}")];
        var modules = new StringBuilder(await File.ReadAllTextAsync(Path.Combine(f.Root, ".gitmodules"), f.Token));
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.Combine(f.Root, path));
            modules.Append("[submodule \"").Append(path).Append("\"]\n\tpath = ").Append(path).Append("\n\turl = https://example.invalid/m.git\n");
        }
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".gitmodules"), modules.ToString(), f.Token);
        var input = Encoding.UTF8.GetBytes(string.Concat(paths.Select(path => "160000 " + head + " 0\t" + path + "\n")));
        await f.Runner.RunAsync(f.Root, ["update-index", "--index-info"], f.Token, standardInput: input);

        var listed = (await f.ReadAsync()).SubmodulesToUpdate;

        Assert.Equal(paths, listed);
    }

    // A submodule's address comes from the repository itself, so a local repository is never cloned for it, as Git
    // allows that only to the user directly.
    [Fact]
    public async Task WhenAMissingSubmoduleIsALocalRepository_ShouldRefuseToCloneIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);

        var result = await Writer(f).ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync(clone)), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, SubmoduleText.NotUpdated), (result.Outcome, result.Message));
        Assert.Contains("transport 'file' not allowed", result.Details);
    }

    // A repository whose own configuration allows local addresses has its local submodule cloned, as plain Git would.
    [Fact]
    public async Task WhenTheRepositoryAllowsLocalAddresses_ShouldCloneALocalSubmodule()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneAsync(f);
        await f.GitAtAsync(clone, "config", "protocol.file.allow", "always");

        var result = await Writer(f).ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync(clone)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Empty((await f.ReadAsync(clone)).SubmodulesToUpdate);
    }

    // A submodule Git cannot clone leaves the update unfinished, and the result says so with Git's own words.
    [Fact]
    public async Task WhenASubmoduleCannotBeCloned_ShouldRejectTheUpdate()
    {
        var (f, server) = await SubmoduleFixture.CreateServedAsync();
        using var _ = server;
        using var __ = f;
        var clone = await CloneAsync(f);
        Directory.Move(SubmoduleFixture.Origin(f), SubmoduleFixture.Origin(f) + "-gone");

        var result = await Writer(f).ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync(clone)), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, SubmoduleText.NotUpdated), (result.Outcome, result.Message));
        Assert.NotEqual("", result.Details);
        Assert.Equal(["sub"], (await f.ReadAsync(clone)).SubmodulesToUpdate);
    }
}
