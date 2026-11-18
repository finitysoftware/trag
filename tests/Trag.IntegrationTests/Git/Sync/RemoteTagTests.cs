using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Sync;

public sealed class RemoteTagTests
{
    // An annotated and a lightweight tag arrive as they are, and nothing but the tag is sent.
    [Fact]
    public async Task WhenLocalTagsArePushed_ShouldPutTheSameTagsOnTheRemoteAndSayATagIsThereWhenPushedAgain()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await f.Git(f.One, "tag", "--annotate", "--message=First release", "v1.0");
        await f.Git(f.One, "tag", "light");
        await f.Commit(f.One, "not pushed");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var pushed = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "v1.0", RemoteTagAction.Push), f.Token);
        var light = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "light", RemoteTagAction.Push), f.Token);
        var again = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "v1.0", RemoteTagAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + " " + pushed.Details);
        Assert.Equal(RemoteTagText.Pushed("v1.0", "origin"), pushed.Message);
        Assert.True(light.Outcome == GitWriteOutcome.Succeeded, light.Message + " " + light.Details);
        Assert.Equal((GitWriteOutcome.Succeeded, RemoteTagText.AlreadyThere("v1.0", "origin")), (again.Outcome, again.Message));
        Assert.Equal(await f.Git(f.One, "rev-parse", "refs/tags/v1.0"), await f.Git(f.Bare, "rev-parse", "refs/tags/v1.0"));
        Assert.Equal(await f.Git(f.One, "rev-parse", "refs/tags/light"), await f.Git(f.Bare, "rev-parse", "refs/tags/light"));
        Assert.Equal("refs/heads/trunk\nrefs/tags/light\nrefs/tags/v1.0", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
        Assert.NotEqual(await f.Head(f.One), await f.Git(f.Bare, "rev-parse", "refs/heads/trunk"));
    }

    // A tag named with a slash goes by its whole ref, so Git never reads its name as a branch or a shorter tag.
    [Fact]
    public async Task WhenATagWithASlashIsPushed_ShouldUseTheFullRefspec()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "tag", "release/v1");

        var pushed = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteTagRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "release/v1", RemoteTagAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + " " + pushed.Details);
        Assert.Equal("refs/heads/trunk\nrefs/tags/release/v1", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
    }

    [Fact]
    public async Task WhenTheRemoteHasAnotherTagOfTheNameOrThePushCannotBeMade_ShouldRefuseAndChangeNothing()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await f.Commit(f.Two, "theirs"); await f.Git(f.Two, "tag", "v1.0"); await f.Git(f.Two, "push", "origin", "refs/tags/v1.0");
        var theirs = await f.Git(f.Two, "rev-parse", "refs/tags/v1.0");
        await f.Git(f.One, "tag", "v1.0");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var different = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "v1.0", RemoteTagAction.Push), f.Token);
        var missing = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "nope", RemoteTagAction.Push), f.Token);
        var nowhere = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "nowhere", "v1.0", RemoteTagAction.Push), f.Token);
        var invalid = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "bad..name", RemoteTagAction.Push), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RemoteTagText.Different("v1.0", "origin")), (different.Outcome, different.Message));
        Assert.Equal((GitWriteOutcome.Rejected, RemoteTagText.Missing("nope")), (missing.Outcome, missing.Message));
        Assert.Equal((GitWriteOutcome.Rejected, RemoteTagText.RemoteGone), (nowhere.Outcome, nowhere.Message));
        Assert.Equal((GitWriteOutcome.Rejected, RemoteTagText.NameInvalid), (invalid.Outcome, invalid.Message));
        Assert.Equal(theirs, await f.Git(f.Bare, "rev-parse", "refs/tags/v1.0"));
    }

    // Deleting on the remote leaves the local tag alone; one the remote no longer has is nothing to delete.
    [Fact]
    public async Task WhenATagIsDeletedOnTheRemote_ShouldVerifyItIsGoneThereAndKeepItHere()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await f.Git(f.One, "tag", "v1.0"); await f.Git(f.One, "push", "origin", "refs/tags/v1.0");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var deleted = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "v1.0", RemoteTagAction.Delete), f.Token);
        var gone = await writer.ExecuteAsync(new RemoteTagRequest(snapshot, "origin", "v1.0", RemoteTagAction.Delete), f.Token);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal(RemoteTagText.Deleted("v1.0", "origin"), deleted.Message);
        Assert.Equal((GitWriteOutcome.Succeeded, RemoteTagText.AlreadyGone("v1.0", "origin")), (gone.Outcome, gone.Message));
        Assert.Equal("refs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
        Assert.Equal("v1.0", await f.Git(f.One, "tag", "--list"));
    }

    // A teammate replaces the tag on the remote after Trag read it and before the deletion arrives: the deletion goes only
    // while the tag is still the one Trag read, so the teammate's tag stays.
    [Fact]
    public async Task WhenTheRemoteTagChangesBeforeTheDeletionArrives_ShouldRefuseAndKeepTheNewTag()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "tag", "v1.0"); await f.Git(f.One, "push", "origin", "refs/tags/v1.0");
        await f.Git(f.Two, "pull"); await f.Commit(f.Two, "teammate"); await f.Git(f.Two, "tag", "--force", "v1.0");
        var teammate = await f.Head(f.Two);
        var pushed = false;
        var between = new OnReport(text =>
        {
            if (pushed || text != RemoteTagText.Deleting("v1.0", "origin")) return;
            f.Git(f.Two, "push", "--force", "origin", "refs/tags/v1.0").GetAwaiter().GetResult();
            pushed = true;
        });

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteTagRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "v1.0", RemoteTagAction.Delete) { Progress = between }, f.Token);

        Assert.True(pushed);
        Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(teammate, await f.Git(f.Bare, "rev-parse", "refs/tags/v1.0"));
    }

    private sealed class OnReport(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
