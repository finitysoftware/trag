using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// The outcome of every synchronization stopped where the stop is deterministic (SyncInterruption), and what the remote
// and the branch say afterwards. A stop before anything was sent, or while a fetch or a transfer to a server waits,
// changes nothing. A stop while Git updates One's refs comes after the remote took the push, so the verified push
// succeeds, while a fetch is Cancelled and a merge is kept for review. A stop while the bare remote's receive-pack waits
// in its pre-receive hook or holds its own ref locks is Cancelled when receive-pack was stopped with the push, and a
// verified success when it went on to finish the push, as it sometimes does; the locks a stopped receive-pack leaves in
// the remote stay there on purpose. A stop during the verification is answered by the
// verification. Kept apart from SyncInterruptionMatrixTests so that the two run side by side.
public sealed class SyncInterruptionOutcomeTests
{
    [Theory]
    [InlineData("fetch", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("publish", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("remote-branch-delete", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("remote-tag-push", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("lfs-download", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("submodule-update", "before-start", GitWriteOutcome.Cancelled)]
    [InlineData("fetch", "remote-waits", GitWriteOutcome.Cancelled)]
    [InlineData("pull-ff", "remote-waits", GitWriteOutcome.Cancelled)]
    [InlineData("push", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("force-push", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("publish", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-branch-delete", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-push", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-delete", "remote-waits", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("pull-lfs", "remote-waits", GitWriteOutcome.Cancelled)]
    [InlineData("push-lfs", "remote-waits", GitWriteOutcome.Cancelled)]
    [InlineData("submodule-update", "remote-waits", GitWriteOutcome.Cancelled)]
    [InlineData("fetch", "local-refs-locked", GitWriteOutcome.Cancelled)]
    [InlineData("fetch-tag", "local-refs-locked", GitWriteOutcome.Cancelled)]
    [InlineData("pull-ff", "local-refs-locked", GitWriteOutcome.Uncertain)]
    [InlineData("pull-merge", "local-refs-locked", GitWriteOutcome.Uncertain)]
    [InlineData("push", "local-refs-locked", GitWriteOutcome.Succeeded)]
    [InlineData("force-push", "local-refs-locked", GitWriteOutcome.Succeeded)]
    [InlineData("publish", "local-refs-locked", GitWriteOutcome.Succeeded)]
    [InlineData("remote-branch-delete", "local-refs-locked", GitWriteOutcome.Succeeded)]
    [InlineData("push", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("force-push", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("publish", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-branch-delete", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-push", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-delete", "remote-refs-locked", GitWriteOutcome.Cancelled, GitWriteOutcome.Succeeded)]
    [InlineData("pull-ff", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("pull-merge", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("push", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("force-push", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("publish", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("remote-branch-delete", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-push", "verifying", GitWriteOutcome.Succeeded)]
    [InlineData("remote-tag-delete", "verifying", GitWriteOutcome.Succeeded)]
    public async Task WhenASyncIsInterrupted_ShouldReportTheOutcomeTheRemoteAndTheBranchConfirm(string operation, string point, params GitWriteOutcome[] expected)
    {
        using var silent = new SilentServer();
        using var f = await SyncFixture.Create();
        var scenario = await SyncInterruption.PrepareAsync(f, operation, silent);
        var (remote, branch, config) = await StateAsync(f);

        var result = await scenario.StopAsync(f, point, silent);

        Assert.True(expected.Contains(result.Outcome), result.Outcome + ": " + result.Message + result.Details);
        var after = await StateAsync(f);
        switch (result.Outcome)
        {
            case GitWriteOutcome.Succeeded:
                await scenario.Done!();
                break;
            // A merge stopped while it records its result keeps the index it wrote for review, and the branch where it was.
            case GitWriteOutcome.Uncertain:
                Assert.Equal(branch, after.Branch);
                Assert.Single(Directory.EnumerateFiles(Path.Combine(f.One, ".git"), "trag-write-*.index"));
                break;
            default:
                Assert.Equal((remote, branch), (after.Remote, after.Branch));
                if (point == "before-start") Assert.Equal(config, after.Config);
                break;
        }
    }

    // Every ref of the remote, One's branches and tags with the commit it has checked out, and One's own configuration.
    private static async Task<(string Remote, string Branch, string Config)> StateAsync(SyncFixture f) => (
        await f.Git(f.Bare, "for-each-ref", "--format=%(refname) %(objectname)"),
        await f.Head(f.One) + "\n" + await f.Git(f.One, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads", "refs/tags"),
        await f.Git(f.One, "config", "--local", "--list"));
}
