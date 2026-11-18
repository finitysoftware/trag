using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// What the rebase side of the merge dialog reads before anything changes, with real Git: the counts on both sides, the
// commits Git's plan replays (no merge commits, no change the onto branch already has) newest first, the files both
// sides changed since they parted, how many of its commits the branch's upstream already has, and whether that upstream
// is its remote's default branch.
public sealed class RebasePreviewTests
{
    // Without a common commit there is no point the two sides parted at: the files both changed are unknown, and the
    // preview still reads, every commit of the branch replayed.
    [Fact]
    public async Task WhenTheBranchesShareNoHistory_ShouldLeaveTheSharedFilesUnknown()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "--orphan", "other");
        await f.WriteAsync("other.txt", "other\n"); await f.CommitAsync("Other root", "other.txt");

        var preview = await new CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);

        Assert.True(preview.IsRead);
        Assert.Null(preview.BothSides);
        Assert.Equal(1, preview.ReplayedCount);
    }

    [Fact]
    public async Task WhenTheBranchDivergedFromTheOntoBranch_ShouldNameItsCommitsTheSharedFilesWhatItsUpstreamHasAndWhetherThatIsTheDefault()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("dir č/shared file.txt", "base\n"); await f.WriteAsync("own.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("dir č/shared file.txt", "feature\n"); await f.CommitAsync("Feature 1");
        await f.WriteAsync("own.txt", "feature\n"); await f.CommitAsync("Feature 2");
        // The upstream has the first two commits of the branch; the third was never pushed.
        await f.GitAsync("config", "remote.origin.url", "https://fetch.example.invalid/origin");
        await f.GitAsync("config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await f.GitAsync("config", "branch.feature.remote", "origin");
        await f.GitAsync("config", "branch.feature.merge", "refs/heads/feature");
        await f.GitAsync("update-ref", "refs/remotes/origin/feature", "HEAD");
        await f.WriteAsync("new.txt", "feature\n"); var head = await f.CommitAsync("Feature 3");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("dir č/shared file.txt", "main\n"); await f.CommitAsync("Main 1");
        await f.WriteAsync("main.txt", "main\n"); var main = await f.CommitAsync("Main 2");
        await f.GitAsync("switch", "feature");

        var preview = await new CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);
        Assert.Equal((head, main), (preview.Head, preview.OntoHead));
        Assert.Equal((3, 2), (preview.ReplayedCount, preview.OntoCount));
        Assert.Equal(["Feature 3", "Feature 2", "Feature 1"], preview.Replayed.Select(commit => commit.Subject));
        Assert.Equal(["dir č/shared file.txt"], preview.BothSides!.Select(path => path.Display));
        Assert.Equal(("origin/feature", 2), (preview.Upstream, preview.Published));
        Assert.Equal(("feature\n", (bool?)null), (await File.ReadAllTextAsync(Path.Combine(f.Root, "dir č", "shared file.txt"), f.Token), preview.UpstreamIsDefault));
        Assert.Empty((await f.ReadAsync()).Files);

        // origin/HEAD names the remote's default branch.
        var reader = new CliBranchReader(f.Runner);
        await f.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/feature");
        Assert.True((await reader.PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token)).UpstreamIsDefault);
        await f.GitAsync("update-ref", "refs/remotes/origin/main", main);
        await f.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        Assert.False((await reader.PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token)).UpstreamIsDefault);
    }

    [Fact]
    public async Task WhenTheBranchHasMergeCommitsAndChangesTheOntoBranchAlreadyHas_ShouldLeaveThemOutAsGitsPlanDoes()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "grpc");
        await f.WriteAsync("g.txt", "g\n"); var grpc = await f.CommitAsync("grpc own");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("m1.txt", "1\n"); await f.CommitAsync("Main 1");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("s.txt", "s\n"); await f.CommitAsync("Side 1");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("m2.txt", "2\n"); await f.CommitAsync("Main 2");
        await f.GitAsync("merge", "--no-ff", "side", "-m", "Merge branch 'side'");
        await f.GitAsync("cherry-pick", grpc);

        var preview = await new CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/grpc", f.Token);
        Assert.Equal((3, 1, 1, 5, 1), (preview.ReplayedCount, preview.Merges, preview.AlreadyOnto, preview.OwnCount, preview.OntoCount));
        Assert.Equal(["Main 1", "Main 2", "Side 1"], preview.Replayed.Select(commit => commit.Subject).Order());
    }

    [Fact]
    public async Task WhenTheBranchIsAheadOrBehindTheOntoBranch_ShouldBeUpToDateWhenAheadAndOnlyMoveWhenBehind()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("feature.txt", "1\n"); await f.CommitAsync("Feature");
        var reader = new CliBranchReader(f.Runner);

        var ahead = await reader.PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);
        Assert.True(ahead.IsUpToDate); Assert.Equal(1, ahead.ReplayedCount);
        Assert.Null(ahead.Upstream);

        await f.GitAsync("switch", "main");
        var behind = await reader.PreviewRebaseAsync(f.Root, "refs/heads/feature", f.Token);
        Assert.True(behind.OnlyMoves); Assert.Empty(behind.Replayed);
        Assert.Empty(behind.BothSides!);
    }

    // The listed commits are the newest ones Git would replay: a newer change the onto branch already has is skipped and
    // the list still holds as many as it may.
    [Fact]
    public async Task WhenTheBranchHasMoreCommitsThanTheListShowsAndOneIsOnTheOntoBranch_ShouldListTheNewestReplayedOnes()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        var commits = new List<string>();
        for (var i = 1; i <= 7; i++) { await f.WriteAsync("f" + i + ".txt", i + "\n"); commits.Add(await f.CommitAsync("Feature " + i)); }
        await f.GitAsync("switch", "main");
        await f.GitAsync("cherry-pick", commits[5]);
        await f.GitAsync("switch", "feature");

        var preview = await new CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);
        Assert.Equal((6, 1), (preview.ReplayedCount, preview.AlreadyOnto));
        Assert.Equal(["Feature 7", "Feature 5", "Feature 4", "Feature 3", "Feature 2"], preview.Replayed.Select(commit => commit.Subject));
    }

    [Fact]
    public async Task WhenTheOntoIsNotARef_ShouldRefuseBeforeGitRuns()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Base");
        var reader = new CliBranchReader(f.Runner);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.PreviewRebaseAsync(f.Root, "main", f.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.PreviewRebaseAsync(f.Root, "--output=x", f.Token));
    }

    // A submodule both sides moved is a path both changed whatever the configuration ignores, as the status lists it;
    // Git's diff would leave it out.
    [Theory]
    [InlineData("diff.ignoreSubmodules")]
    [InlineData("submodule.sub.ignore")]
    public async Task WhenTheConfigurationIgnoresSubmodules_ShouldStillNameASubmoduleBothSidesMoved(string key)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var two = await f.GitAtAsync(sub, "rev-parse", "HEAD");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub three");
        await f.GitAtAsync(sub, "checkout", "--quiet", two);
        await f.CommitAsync("Main moves sub", "sub");
        await f.GitAsync("switch", "-c", "feature", "HEAD~1");
        await f.GitAtAsync(sub, "checkout", "--quiet", "main");
        await f.CommitAsync("Feature moves sub", "sub");
        await f.GitAsync("config", key, "all");

        var preview = await new CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);

        Assert.Equal(["sub"], preview.BothSides!.Select(path => path.Display));
    }
}
