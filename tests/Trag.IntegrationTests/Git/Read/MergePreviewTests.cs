using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// What the merge dialog reads before anything changes, with real Git: the counts on both sides, the newest incoming
// commits first and at most five of them, and the conflicted paths merge-tree predicts, spaces and accents included.
public sealed class MergePreviewTests
{
    [Fact]
    public async Task WhenOneBranchIsAheadOfTheOther_ShouldFastForwardOneWayWithTheNewestCommitsFirstAndBeUpToDateTheOtherWay()
    {
        using var f = await GitFixture.CreateAsync("main");
        var main = await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        for (var i = 1; i <= 7; i++) { await f.WriteAsync("feature.txt", i + "\n"); await f.CommitAsync("Feature " + i); }
        var feature = await f.GitAsync("rev-parse", "HEAD");
        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(f.Root, "refs/heads/main", "refs/heads/feature", f.Token);
        Assert.Equal((main, feature), (preview.IntoHead, preview.FromHead));
        Assert.Equal((7, 0), (preview.IncomingCount, preview.OwnCount));
        Assert.True(preview.CanFastForward); Assert.False(preview.IsUpToDate);
        Assert.Equal(["Feature 7", "Feature 6", "Feature 5", "Feature 4", "Feature 3"], preview.Incoming.Select(commit => commit.Subject));
        Assert.Equal(2, preview.MoreCount);
        Assert.Empty(preview.Conflicts!);

        var back = await new CliBranchReader(f.Runner).PreviewMergeAsync(f.Root, "refs/heads/feature", "refs/heads/main", f.Token);
        Assert.True(back.IsUpToDate); Assert.Empty(back.Incoming); Assert.Equal(7, back.OwnCount);
    }

    [Fact]
    public async Task WhenTheBranchesDiverged_ShouldNameEveryFileTheMergeWouldLeaveInConflictWithoutTouchingTheWorktree()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("dir č/a file.txt", "base\n"); await f.WriteAsync("clean.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("dir č/a file.txt", "feature\n"); await f.WriteAsync("new.txt", "feature\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("dir č/a file.txt", "main\n"); await f.CommitAsync("Main");
        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(f.Root, "refs/heads/main", "refs/heads/feature", f.Token);
        Assert.Equal((1, 1), (preview.IncomingCount, preview.OwnCount));
        Assert.False(preview.CanFastForward); Assert.True(preview.HasConflicts); Assert.False(preview.SharesNoCommit);
        Assert.Equal(["dir č/a file.txt"], preview.Conflicts!.Select(path => path.Display));
        Assert.Equal("main\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "dir č", "a file.txt"), f.Token));
        Assert.Empty((await f.ReadAsync()).Files);
    }

    // merge-tree answers neither "clean" nor "these conflict" for histories with no common commit; the preview then
    // knows nothing about conflicts, which is not the same as knowing there are none, and says the branches share no
    // commit.
    [Fact]
    public async Task WhenMergeTreeCannotAnswer_ShouldLeaveTheConflictsUnknown()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Base");
        await f.GitAsync("switch", "--orphan", "other");
        await f.WriteAsync("other.txt", "other\n"); await f.CommitAsync("Other root", "other.txt");
        await f.GitAsync("switch", "main");

        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(f.Root, "refs/heads/main", "refs/heads/other", f.Token);

        Assert.Null(preview.Conflicts);
        Assert.False(preview.HasConflicts);
        Assert.True(preview.SharesNoCommit);
    }

    [Fact]
    public async Task WhenEitherSideIsNotABranchRef_ShouldRefuseBeforeGitRuns()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Base");
        var reader = new CliBranchReader(f.Runner);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.PreviewMergeAsync(f.Root, "main", "refs/heads/main", f.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.PreviewMergeAsync(f.Root, "refs/remotes/origin/main", "refs/heads/main", f.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.PreviewMergeAsync(f.Root, "refs/heads/main", "--output=x", f.Token));
    }
}
