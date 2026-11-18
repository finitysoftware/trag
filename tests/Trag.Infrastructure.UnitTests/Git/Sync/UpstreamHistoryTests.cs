using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// Whether a branch holds commits its upstream dropped, asked of real Git, since the answer lives in a reflog.
public sealed class UpstreamHistoryTests
{
    // A clone writes no reflog for its remote-tracking refs, so the fetch that brings the rewritten upstream records the
    // tip the clone brought only as its entry's old value.
    [Fact]
    public async Task WhenTheUpstreamIsRewrittenBeforeTheFirstFetchOfAClone_ShouldSeeTheCommitItDropped()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var hub = Path.Combine(fixture.Container, "hub.git");
        var clone = Path.Combine(fixture.Container, "clone");
        await fixture.CommitAsync("Start");
        await fixture.CommitAsync("Shared work");
        await fixture.GitAtAsync(fixture.Container, "init", "-q", "--bare", "hub.git");
        await fixture.GitAsync("push", "-q", hub, "main");
        await fixture.GitAtAsync(fixture.Container, "clone", "-q", "--branch", "main", hub, clone);
        await fixture.GitAsync("commit", "-q", "--amend", "--allow-empty", "-m", "Shared work, reworded");
        await fixture.GitAsync("push", "-q", "--force", hub, "main");
        await fixture.GitAtAsync(clone, "fetch", "-q", "origin");
        var head = await fixture.GitAtAsync(clone, "rev-parse", "HEAD");
        var tip = await fixture.GitAtAsync(clone, "rev-parse", "refs/remotes/origin/main");

        var dropped = await UpstreamHistory.DroppedAsync(new PresetGitRunner(fixture.Runner, LocalGitPreset.Reading), clone, head,
            "refs/remotes/origin/main", tip, ahead: 1, fixture.Token);

        Assert.True(dropped);
    }
}
