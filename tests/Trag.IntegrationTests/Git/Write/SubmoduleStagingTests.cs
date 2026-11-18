using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Staging in a repository with a submodule: other files and the submodule's own move to another commit stage as files
// do; a folder that is a repository of its own is staged only as a submodule .gitmodules lists.
public sealed class SubmoduleStagingTests
{
    private static async Task<GitWriteResult> StageAsync(GitFixture f, string name)
    {
        var snapshot = await f.ReadAsync();
        return await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(snapshot, [snapshot.Files.Single(file => file.Path.Display.TrimEnd('/') == name).Path]), f.Token);
    }

    [Fact]
    public async Task WhenTheRepositoryHasASubmodule_ShouldStageOtherFilesAsUsual()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.WriteAsync("a.txt", "changed\n");

        var result = await StageAsync(f, "a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("a.txt", await f.GitAsync("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task WhenTheSubmoduleMovedToAnotherCommit_ShouldStageItsNewCommit()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var moved = await f.GitAtAsync(sub, "rev-parse", "HEAD");

        var result = await StageAsync(f, "sub");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.StartsWith("160000 " + moved + " ", await f.GitAsync("ls-files", "--stage", "--", "sub"));
    }

    // Git stages nothing of a submodule whose only changes are files inside it: they are committed in the submodule, which
    // the refusal says, and the index stays as it was.
    [Fact]
    public async Task WhenOnlyTheSubmodulesOwnFilesChanged_ShouldRefuseToStageItAndSayWhy()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.WriteAsync("sub/inner.txt", "untracked inside\n");

        var result = await StageAsync(f, "sub");

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.SubmoduleChangesNotStaged("sub")), (result.Outcome, result.Message));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only", "--ignore-submodules=none"));
    }

    // A moved submodule and a file staged in one request are both staged.
    [Fact]
    public async Task WhenASubmodulePointerAndAFileAreStagedTogether_ShouldStageBoth()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "commit", "--allow-empty", "-m", "Sub two");
        await f.WriteAsync("a.txt", "changed\n");
        var snapshot = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(snapshot, [.. snapshot.Files.Select(file => file.Path)]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["a.txt", "sub"], (await f.GitAsync("diff", "--cached", "--name-only", "--ignore-submodules=none")).Split('\n'));
    }

    // A stash of a tree where a submodule moved and a file changed keeps the file; the submodule stays where it is, as a
    // stash keeps nothing of a submodule's checkout.
    [Fact]
    public async Task WhenAStashIsPushedWithAMovedSubmoduleAndAFile_ShouldStashOnlyTheFile()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var moved = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.WriteAsync("a.txt", "changed\n");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("file only"), StashAction.Push), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("a", (await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"))).TrimEnd());
        Assert.Equal(moved, await f.GitAtAsync(sub, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task WhenAFolderIsARepositoryTheSubmodulesDoNotList_ShouldRefuseToStageIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var nested = Path.Combine(f.Root, "nested");
        Directory.CreateDirectory(nested);
        await f.GitAtAsync(nested, "init", "--initial-branch=main");
        await SubmoduleFixture.IdentifyAsync(f, nested);
        await f.GitAtAsync(nested, "commit", "--allow-empty", "-m", "Nested");

        var result = await StageAsync(f, "nested");

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.SubmodulesNotStaged), (result.Outcome, result.Message));
        Assert.Equal("", await f.GitAsync("ls-files", "--stage", "--", "nested"));
    }

    // A repository committed long ago without .gitmodules stays as it is; it does not stop other files from staging.
    [Fact]
    public async Task WhenAnUnlistedRepositoryWasCommittedEarlier_ShouldStillStageOtherFiles()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        await f.GitAsync("update-index", "--add", "--cacheinfo", "160000," + head + ",old-module");
        await f.GitAsync("commit", "-m", "Old embedded repository");
        await f.WriteAsync("a.txt", "changed\n");

        var result = await StageAsync(f, "a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
    }

    // A repository cloned into the folder and listed in .gitmodules by hand is a submodule being added.
    [Fact]
    public async Task WhenAFolderIsARepositoryGitmodulesLists_ShouldStageItAsASubmodule()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var second = Path.Combine(f.Root, "second");
        await f.GitAsync("-c", "protocol.file.allow=always", "clone", "--", new Uri(SubmoduleFixture.Origin(f)).AbsoluteUri, "second");
        await f.GitAsync("config", "--file", ".gitmodules", "submodule.second.path", "second");
        await f.GitAsync("config", "--file", ".gitmodules", "submodule.second.url", new Uri(SubmoduleFixture.Origin(f)).AbsoluteUri);
        var head = await f.GitAtAsync(second, "rev-parse", "HEAD");

        var result = await StageAsync(f, "second");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.StartsWith("160000 " + head + " ", await f.GitAsync("ls-files", "--stage", "--", "second"));
    }
}
