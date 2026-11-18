using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.IntegrationTests.Git.Sync;

// A transfer has no limit of its own: it goes on as long as the server keeps saying something, and stops only after the
// quiet limit without a word, which it says. The server here is origin's upload-pack, started late by a shell.
public sealed class SyncQuietTests
{
    // The state is read first, as the shell does, from the repository alone; only the fetch meets the slow server.
    private static async Task<GitWriteResult> FetchAsync(SyncFixture f, string uploadPack)
    {
        await f.Git(f.One, "config", "remote.origin.uploadpack", uploadPack);
        var sync = new GitSyncFacade(new GitProcessRunner(), f.Reader, quietLimit: TimeSpan.FromSeconds(2));
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var state = await sync.ReadAsync(snapshot, "origin", null, f.Token);
        return await sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Fetch), f.Token);
    }

    [Fact]
    public async Task WhenTheServerKeepsTalkingLongerThanTheQuietLimit_ShouldFetch()
    {
        using var f = await SyncFixture.Create();

        var result = await FetchAsync(f, "for i in 1 2 3 4 5; do echo waiting $i >&2; sleep 1; done; git-upload-pack");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
    }

    [Fact]
    public async Task WhenTheServerSaysNothingForTheQuietLimit_ShouldStopAndSaySo()
    {
        using var f = await SyncFixture.Create();

        var result = await FetchAsync(f, "sleep 8; git-upload-pack");

        Assert.Equal((GitWriteOutcome.Cancelled, SyncOutcomeText.TimedOut), (result.Outcome, result.Message));
        Assert.StartsWith("The server sent nothing for 2 minutes", SyncOutcomeText.TimedOut);
    }
}
