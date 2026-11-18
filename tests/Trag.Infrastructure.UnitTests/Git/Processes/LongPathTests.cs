using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// Git for Windows opens a path past MAX_PATH only under core.longpaths, which the fixture sets to false, so no machine
// configuration hides what Git does without it: it reads a tracked file there as deleted and stages that deletion, while
// the file stays on disk, lists no untracked file under such a folder, and only warns and exits 0. The file is committed
// with the setting, as a clone made with it or another tool leaves it.
public sealed class LongPathTests
{
    private static readonly string Deep = TestNames.Find("deep-past-260").Path;

    private static async Task<GitFixture> RepositoryAsync()
    {
        var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.longpaths", "false");
        return f;
    }

    private static async Task CommitDeepFileAsync(GitFixture f)
    {
        await f.WriteAsync(Deep, "deep\n");
        await f.GitAsync("-c", "core.longpaths=true", "add", "--", Deep);
        await f.GitAsync("-c", "core.longpaths=true", "commit", "-m", "Deep");
    }

    [Fact]
    public async Task WhenACommittedFileLiesPastMaxPath_ShouldReadAsUnchanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await RepositoryAsync();
        await CommitDeepFileAsync(f);

        Assert.Empty((await f.ReadAsync()).Files);
    }

    [Fact]
    public async Task WhenAChangedFilePastMaxPathIsStaged_ShouldStageTheChangeAndNotADeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await RepositoryAsync();
        await CommitDeepFileAsync(f);
        await f.WriteAsync(Deep, "changed\n");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(await f.ReadAsync(), [new(Encoding.UTF8.GetBytes(Deep))]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("M\t" + Deep, await f.GitAsync("-c", "core.longpaths=true", "diff", "--cached", "--name-status"));
    }

    [Fact]
    public async Task WhenAnUntrackedFileLiesPastMaxPath_ShouldListIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await RepositoryAsync();
        await f.WriteAsync(Deep, "deep\n");

        Assert.Equal([(Deep, true)], (await f.ReadAsync()).Files.Select(file => (file.Path.Display, file.IsUntracked)));
    }
}
