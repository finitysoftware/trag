using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A submodule's row shows what Git says of it: the commits between the one recorded and the one it is at, and a line
// when its own working tree has changes; in a commit, the commits the commit moved it across.
public sealed class SubmoduleDiffTests
{
    private static async Task<(GitFixture Fixture, string Sub)> MovedAsync()
    {
        var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub three");
        return (f, sub);
    }

    private static async Task<FileStatus> RowAsync(GitFixture f) => (await f.ReadAsync()).Files.Single(file => file.Path.Display == "sub");

    [Fact]
    public async Task WhenTheSubmoduleMovedInTheWorkingTree_ShouldListTheCommitsSinceTheRecordedOne()
    {
        var (f, sub) = await MovedAsync();
        using var _ = f;
        await f.WriteAsync(Path.Combine("sub", "f.txt"), "x\n");
        await f.GitAtAsync(sub, "add", "f.txt");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, await RowAsync(f), false, false, f.Token);

        Assert.Matches("^Submodule sub [0-9a-f]{7,}\\.\\.[0-9a-f]{7,}:\\n  > Sub three\\n  > Sub two$", string.Join("\n", diff.Message.Split('\n').Where(line => !line.Contains("modified content"))));
        Assert.Contains("Submodule sub contains modified content", diff.Message);
    }

    [Fact]
    public async Task WhenTheSubmodulesMoveIsStaged_ShouldListTheCommitsAgainstTheLastCommit()
    {
        var (f, _) = await MovedAsync();
        using var _f = f;
        await f.GitAsync("add", "sub");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, await RowAsync(f), true, false, f.Token);

        Assert.Contains("  > Sub three\n  > Sub two", diff.Message);
    }

    // diff.ignoreSubmodules=all or submodule.<name>.ignore=all would have Git's diff say nothing of the submodule; the row
    // Git's status lists still shows the commits it moved across, in the working tree and staged.
    [Theory]
    [InlineData("diff.ignoreSubmodules", false)]
    [InlineData("diff.ignoreSubmodules", true)]
    [InlineData("submodule.sub.ignore", false)]
    [InlineData("submodule.sub.ignore", true)]
    public async Task WhenTheConfigurationIgnoresSubmodulesInDiffs_ShouldStillListTheCommitsTheSubmoduleMovedAcross(string key, bool staged)
    {
        var (f, _) = await MovedAsync();
        using var _f = f;
        if (staged) await f.GitAsync("add", "sub");
        await f.GitAsync("config", key, "all");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, await RowAsync(f), staged, false, f.Token);

        Assert.Contains("  > Sub three\n  > Sub two", diff.Message);
    }

    // A diff.external tool is the user's own way to read diffs in a terminal; the row still shows Git's summary of the move.
    [Fact]
    public async Task WhenTheConfigurationNamesAnExternalDiff_ShouldStillListTheCommitsTheSubmoduleMovedAcross()
    {
        var (f, _) = await MovedAsync();
        using var _f = f;
        await f.GitAsync("config", "diff.external", "echo external");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, await RowAsync(f), false, false, f.Token);

        Assert.Contains("  > Sub three\n  > Sub two", diff.Message);
    }

    [Fact]
    public async Task WhenACommitMovedTheSubmodule_ShouldListTheCommitsItMovedItAcross()
    {
        var (f, _) = await MovedAsync();
        using var _f = f;
        await f.GitAsync("add", "sub");
        var moved = await f.CommitAsync("Move sub");
        var reader = new CliHistoryReader(f.Runner);
        var details = await reader.ReadCommitAsync(f.Root, moved, f.Token);

        var diff = await reader.ReadDiffAsync(f.Root, details, details.Files.Single(), false, f.Token);

        Assert.Contains("  > Sub three\n  > Sub two", diff.Message);
    }

    // The status lists a submodule whatever the configuration ignores, so a commit lists it too: submodule.<name>.ignore=all
    // would have Git's diff-tree leave out the submodule a commit moved, and diff.ignoreSubmodules=all is not diff-tree's.
    [Theory]
    [InlineData("diff.ignoreSubmodules")]
    [InlineData("submodule.sub.ignore")]
    public async Task WhenTheConfigurationIgnoresSubmodules_ShouldStillListTheCommitsACommitMovedTheSubmoduleAcross(string key)
    {
        var (f, _) = await MovedAsync();
        using var _f = f;
        await f.GitAsync("add", "sub");
        var moved = await f.CommitAsync("Move sub");
        await f.GitAsync("config", key, "all");
        var reader = new CliHistoryReader(f.Runner);
        var details = await reader.ReadCommitAsync(f.Root, moved, f.Token);

        var diff = await reader.ReadDiffAsync(f.Root, details, Assert.Single(details.Files), false, f.Token);

        Assert.Contains("  > Sub three\n  > Sub two", diff.Message);
    }

    // Read as a repository of its own, a submodule names the repository it belongs to; the superproject names none.
    [Fact]
    public async Task WhenTheSubmoduleIsReadAsARepository_ShouldNameItsSuperproject()
    {
        using var f = await SubmoduleFixture.CreateAsync();

        var superproject = await f.ReadAsync();
        var submodule = await f.ReadAsync(Path.Combine(f.Root, "sub"));

        Assert.Equal((superproject.Root, (string?)null), (submodule.Superproject, superproject.Superproject));
    }

    // Git asks for the submodule's own status without -u, so status.showUntrackedFiles=no there would leave out the files
    // it does not track; the row is still listed with changes of its own.
    [Fact]
    public async Task WhenTheSubmodulesConfigurationHidesUntrackedFiles_ShouldStillListItsRowWithChangesOfItsOwn()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "config", "status.showUntrackedFiles", "no");
        await f.WriteAsync(Path.Combine("sub", "new.txt"), "x\n");

        var row = await RowAsync(f);

        Assert.True(row.SubmoduleHasOwnChanges);
    }

    // Files the submodule does not track change nothing Git compares; the row still says where to look.
    [Fact]
    public async Task WhenTheSubmoduleOnlyHasUntrackedFiles_ShouldSayItsOwnChangesShowInsideIt()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.WriteAsync(Path.Combine("sub", "new.txt"), "x\n");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, await RowAsync(f), false, false, f.Token);

        Assert.Equal(FileDiffText.Submodule, diff.Message);
        Assert.Equal("Submodule · its recorded commit is unchanged; its own changes show when you open it.", FileDiffText.Submodule);
    }
}
