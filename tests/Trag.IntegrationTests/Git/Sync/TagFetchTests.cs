using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.IntegrationTests.Git.Sync;

public sealed class TagFetchTests
{
    // A tag on a commit the fetch brings and one on a commit already here both come with an ordinary fetch.
    [Fact]
    public async Task WhenTheRemoteHasNewTags_ShouldBringThemWithTheFetch()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.Two, "tag", "on-old");
        await f.Commit(f.Two, "theirs");
        await f.Git(f.Two, "tag", "--annotate", "--message=Release", "v1.0");
        await f.Git(f.Two, "push", "origin", "trunk", "refs/tags/v1.0", "refs/tags/on-old");

        var fetched = await f.Execute(SyncAction.Fetch);

        Assert.True(fetched.Outcome == GitWriteOutcome.Succeeded, fetched.Message + " " + fetched.Details);
        Assert.Equal(await f.Git(f.Two, "rev-parse", "refs/tags/v1.0"), await f.Git(f.One, "rev-parse", "refs/tags/v1.0"));
        Assert.Equal(await f.Git(f.Two, "rev-parse", "refs/tags/on-old"), await f.Git(f.One, "rev-parse", "refs/tags/on-old"));
    }

    // The fetch is atomic, so a tag it tried to overwrite would hold the branches back; the tag here is never touched.
    [Fact]
    public async Task WhenTheRemoteHasAnotherTagOfANameUsedHere_ShouldKeepTheOneHereAndStillFetchTheBranches()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "tag", "v1.0");
        var mine = await f.Git(f.One, "rev-parse", "refs/tags/v1.0");
        await f.Commit(f.Two, "theirs");
        await f.Git(f.Two, "tag", "v1.0");
        await f.Git(f.Two, "push", "origin", "trunk", "refs/tags/v1.0");

        var fetched = await f.Execute(SyncAction.Fetch);

        Assert.True(fetched.Outcome == GitWriteOutcome.Succeeded, fetched.Message + " " + fetched.Details);
        Assert.Equal(mine, await f.Git(f.One, "rev-parse", "refs/tags/v1.0"));
        Assert.Equal(await f.Head(f.Two), await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"));
    }

    // remote.origin.tagOpt=--tags makes Git fetch every tag of the remote without the force a tag here would need,
    // so one the remote moved fails the atomic fetch and the branches stay too. The tag here is still never touched, and
    // the message names the tag, says why nothing was fetched and what to do; Git's line for the tag is in the details.
    [Fact]
    public async Task WhenTheRemoteFetchesAllTagsAndMovedOneThatIsHere_ShouldSayWhyNothingWasFetchedAndKeepTheTag()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.Two, "tag", "v1"); await f.Git(f.Two, "push", "origin", "refs/tags/v1");
        await f.Git(f.One, "fetch", "origin", "refs/tags/v1:refs/tags/v1");
        var (mine, trunk) = (await f.Git(f.One, "rev-parse", "refs/tags/v1"), await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"));
        await f.Commit(f.Two, "theirs"); await f.Git(f.Two, "tag", "--force", "v1");
        await f.Git(f.Two, "push", "--force", "origin", "trunk", "refs/tags/v1");
        await f.Git(f.One, "config", "remote.origin.tagOpt", "--tags");

        var fetched = await f.Execute(SyncAction.Fetch);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.TagsWouldBeClobbered(["v1"])), (fetched.Outcome, fetched.Message));
        Assert.Matches(@"v1 +-> v1 +\(would clobber existing tag\)", fetched.Details);
        Assert.Equal((mine, trunk), (await f.Git(f.One, "rev-parse", "refs/tags/v1"), await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk")));
    }
}
