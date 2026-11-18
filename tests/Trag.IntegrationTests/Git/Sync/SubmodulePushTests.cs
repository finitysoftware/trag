using Trag.TestSupport;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.IntegrationTests.Git.Sync;

// A branch that records a submodule commit no remote of the submodule has would leave others a commit they cannot
// fetch: a push or a publish of it waits until that submodule is pushed, and says which one.
public sealed class SubmodulePushTests
{
    // One records a commit of its submodule sub that only sub itself has; sub is a clone of the fixture's origin.
    private static async Task<string> UnpushedSubmoduleAsync(SyncFixture f)
    {
        var sub = Path.Combine(f.One, "sub");
        await f.Git(f.One, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(f.Bare).AbsoluteUri, "sub");
        await f.Git(f.One, "commit", "-m", "Add sub");
        await f.Git(sub, "config", "user.name", "Sync Fixture"); await f.Git(sub, "config", "user.email", "sync@example.invalid");
        await f.Git(sub, "config", "commit.gpgsign", "false");
        await f.Git(sub, "commit", "--allow-empty", "-m", "Sub work");
        await f.Git(f.One, "add", "sub");
        await f.Git(f.One, "commit", "-m", "Move sub");
        return sub;
    }

    [Fact]
    public async Task WhenTheBranchRecordsASubmoduleCommitNoRemoteHas_ShouldRefuseThePushUntilTheSubmoduleIsPushed()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        var sub = await UnpushedSubmoduleAsync(f);

        var refused = await f.Execute(SyncAction.Push);
        var afterRefusal = await f.Head(f.Bare);
        await f.Git(sub, "push", "origin", "HEAD:refs/heads/sub-work");
        var pushed = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.SubmodulesNotPushed(["sub"])), (refused.Outcome, refused.Message));
        Assert.Equal(before, afterRefusal);
        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.Equal(await f.Head(f.One), await f.Head(f.Bare));
    }

    // A submodule with no remote-tracking refs gives nothing to compare with, so Git's push.recurseSubmodules=check leaves
    // it alone; refused, every push would wait for a submodule push the user has no remote for.
    [Fact]
    public async Task WhenTheSubmoduleHasNoRemoteTrackingRefs_ShouldPushAsGitWould()
    {
        using var f = await SyncFixture.Create();
        var sub = await UnpushedSubmoduleAsync(f);
        await f.Git(sub, "remote", "remove", "origin");

        var result = await f.Execute(SyncAction.Push);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.One), await f.Head(f.Bare));
    }

    // A recorded commit the submodule does not have is left alone, as Git leaves it, and does not hide another commit of
    // the submodule no remote has.
    [Fact]
    public async Task WhenTheBranchAlsoRecordsASubmoduleCommitTheSubmoduleLacks_ShouldStillRefuseThePushOfTheUnpushedOne()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        await f.Git(f.One, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(f.Bare).AbsoluteUri, "sub");
        await f.Git(f.One, "commit", "-m", "Add sub");
        await f.Git(f.One, "update-index", "--cacheinfo", "160000," + new string('1', 40) + ",sub");
        await f.Git(f.One, "commit", "-m", "Record a commit sub lacks");
        var sub = Path.Combine(f.One, "sub");
        await f.Git(sub, "config", "user.name", "Sync Fixture"); await f.Git(sub, "config", "user.email", "sync@example.invalid");
        await f.Git(sub, "config", "commit.gpgsign", "false");
        await f.Git(sub, "commit", "--allow-empty", "-m", "Sub work");
        await f.Git(f.One, "add", "sub");
        await f.Git(f.One, "commit", "-m", "Move sub");

        var refused = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.SubmodulesNotPushed(["sub"])), (refused.Outcome, refused.Message));
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // A branch whose commits are signed, read with log.showSignature on: the verification Git prints is no gitlink, and
    // the submodule commit no remote has still keeps the push back.
    [Fact]
    public async Task WhenTheCommitsAreSignedAndLogShowsSignatures_ShouldStillRefuseThePushOfAnUnpushedSubmoduleCommit()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        await SshSigning.UseAsync(args => f.Git(f.One, args), f.Root, f.Token);
        await f.Git(f.One, "config", "log.showSignature", "true");
        await UnpushedSubmoduleAsync(f);

        var refused = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.SubmodulesNotPushed(["sub"])), (refused.Outcome, refused.Message));
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // A user who keeps submodules out of their diffs with diff.ignoreSubmodules=all has Git's log leave out every gitlink;
    // the submodule commit no remote has still keeps the push back.
    [Fact]
    public async Task WhenTheConfigurationIgnoresSubmodulesInDiffs_ShouldStillRefuseThePushOfAnUnpushedSubmoduleCommit()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        await UnpushedSubmoduleAsync(f);
        await f.Git(f.One, "config", "diff.ignoreSubmodules", "all");

        var refused = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.SubmodulesNotPushed(["sub"])), (refused.Outcome, refused.Message));
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // log.showRoot=false has Git's log give a branch's first commit no changes; a submodule commit no remote has, recorded
    // by that first commit, still keeps the publish back.
    [Fact]
    public async Task WhenTheFirstCommitRecordsASubmoduleCommitNoRemoteHasAndLogHidesItsChanges_ShouldRefuseThePublish()
    {
        using var f = await SyncFixture.Create();
        var sub = Path.Combine(f.One, "sub");
        await f.Git(f.One, "config", "log.showRoot", "false");
        await f.Git(f.One, "switch", "--orphan", "fresh");
        await f.Git(f.One, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(f.Bare).AbsoluteUri, "sub");
        await f.Git(sub, "config", "user.name", "Sync Fixture"); await f.Git(sub, "config", "user.email", "sync@example.invalid");
        await f.Git(sub, "config", "commit.gpgsign", "false");
        await f.Git(sub, "commit", "--allow-empty", "-m", "Sub work");
        await f.Git(f.One, "add", "sub");
        await f.Git(f.One, "commit", "-m", "Start with sub");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var result = await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "fresh", true)), f.Token);

        Assert.Equal(PublishOutcomeText.NowTracks("fresh", "origin/fresh") + " " + TransportText.SubmodulesNotPushed(["sub"]), result.Message);
        Assert.Equal("", await f.Git(f.Bare, "branch", "--list", "fresh"));
    }

    // What the remote lacks is what its own tracking refs do not reach, also when the push runs against Trag's verified
    // copy of the remote: an older commit some other push left recording an unpushed submodule commit does not stop a
    // push that records none.
    [Fact]
    public async Task WhenOnlyAnAlreadyPushedCommitRecordsAnUnpushedSubmoduleCommit_ShouldPushThroughAPushAddressOfItsOwn()
    {
        using var f = await SyncFixture.Create(); var other = Path.Combine(f.Root, "publish.git");
        await UnpushedSubmoduleAsync(f);
        await f.Git(f.One, "push", "--recurse-submodules=no", "origin", "trunk");
        await f.Git(f.Root, "clone", "--bare", f.Bare, other);
        await f.Git(f.One, "remote", "set-url", "--push", "origin", other);
        await f.Commit(f.One, "plain");

        var result = await f.Execute(SyncAction.Push);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
    }

    [Fact]
    public async Task WhenAPublishedBranchRecordsASubmoduleCommitNoRemoteHas_ShouldRefuseThePublish()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "topic");
        await UnpushedSubmoduleAsync(f);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var result = await f.Sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true)), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(PublishOutcomeText.NowTracks("topic", "origin/topic") + " " + TransportText.SubmodulesNotPushed(["sub"]), result.Message);
        Assert.Equal("", await f.Git(f.Bare, "branch", "--list", "topic"));
    }

    // A first publish to a remote Trag has no tracking refs of reads the branch's whole history. Each submodule is asked
    // once about every commit of it the history records, so the publish costs as many Git processes for twelve moves of
    // a submodule as for one.
    [Fact]
    public async Task WhenTheFirstPushGoesToARemoteWithNoTrackingRefs_ShouldAskEachSubmoduleOnce()
    {
        var oneMove = await FirstPublishProcessesAsync(1);

        var twelveMoves = await FirstPublishProcessesAsync(12);

        Assert.Equal(oneMove, twelveMoves);
    }

    // The Git processes a publish of topic to an empty remote starts, topic recording so many commits of sub, all of them
    // on sub's own remote.
    private static async Task<int> FirstPublishProcessesAsync(int moves)
    {
        using var f = await SyncFixture.Create();
        var other = Path.Combine(f.Root, "other.git");
        await f.Git(f.Root, "init", "--bare", "--initial-branch=trunk", other);
        await f.Git(f.One, "remote", "add", "other", other);
        await f.Git(f.One, "switch", "-c", "topic");
        var sub = await UnpushedSubmoduleAsync(f);
        for (var move = 1; move < moves; move++)
        {
            await f.Git(sub, "commit", "--allow-empty", "-m", "Sub work " + move);
            await f.Git(f.One, "add", "sub");
            await f.Git(f.One, "commit", "-m", "Move sub " + move);
        }
        await f.Git(sub, "push", "origin", "HEAD:refs/heads/sub-work");
        var processes = 0;
        var sync = new GitSyncFacade(new GitProcessRunner(exited: _ => Interlocked.Increment(ref processes)), f.Reader);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var result = await sync.ExecuteAsync(new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "other", "topic", true)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        return processes;
    }
}
