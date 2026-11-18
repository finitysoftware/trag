using Trag.Domain.Presentation.Text;

namespace Trag.IntegrationTests.Journeys;

// A pushed commit amended, the push Git refuses, the force push with a lease the banner offers, and a second clone
// that pulls afterwards without bringing the replaced commit back, over real Git.
public sealed class AmendAfterPushJourneyTests
{
    [Fact]
    public async Task WhenAPushedCommitIsAmendedAndForcePushed_ShouldReplaceItOnTheRemoteAndNotDuplicateItInAnotherClone()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        var hub = Path.Combine(fixture.Container, "hub.git");
        var peer = Path.Combine(fixture.Container, "peer");
        await journey.Git(root, "checkout", "-q", "-b", "main");
        await fixture.WriteAsync("app.txt", "start\n");
        var start = await fixture.CommitAsync("Start");
        await fixture.WriteAsync("app.txt", "start\nfeature\n");
        var published = await fixture.CommitAsync("Add the featrue");
        await journey.Git(fixture.Container, "init", "-q", "--bare", "hub.git");
        await journey.Git(root, "remote", "add", "origin", hub);
        await journey.Git(root, "push", "-q", "-u", "origin", "main");
        await journey.Git(fixture.Container, "clone", "-q", "--branch", "main", hub, peer);
        Assert.Equal(published, await journey.Git(peer, "rev-parse", "HEAD"));
        await journey.Git(peer, "config", "user.name", "Peer User");
        await journey.Git(peer, "config", "user.email", "peer@example.invalid");
        await shell.Session.OpenRepositoryAsync(root);
        await shell.Sync.SyncCompletion;

        // Amend: the panel warns that the commit is on origin/main already; the amended commit replaces it locally.
        shell.Commit.IsAmending = true;
        await shell.Commit.AmendCompletion;
        Assert.Equal(CommitText.AmendPushed("origin/main"), shell.Commit.AmendWarning);
        Assert.Equal("Add the featrue", shell.Commit.CommitMessage.TrimEnd());
        shell.Commit.CommitMessage = "Add the feature";
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var amended = await journey.Git(root, "rev-parse", "HEAD");
        Assert.NotEqual(published, amended);
        Assert.Equal(start, await journey.Git(root, "rev-parse", "HEAD^"));
        Assert.Equal("Add the feature", await journey.Git(root, "log", "-1", "--format=%s"));
        Assert.Equal(published, await journey.Git(hub, "rev-parse", "refs/heads/main"));
        Assert.Equal((1, 1), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));

        // Push: Git refuses it as not a fast-forward and the remote keeps its commit; the banner offers the force push.
        await shell.Sync.PushCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(TransportText.Behind, shell.StatusBar.Status);
        Assert.Equal(published, await journey.Git(hub, "rev-parse", "refs/heads/main"));
        var banner = shell.FileStatusPage.ForcePush;
        Assert.True(banner.Summary.Shows);
        Assert.Equal("main no longer matches origin/main", banner.Summary.Title);

        // Force push with the lease: the question names the tip it replaces only while it is still there.
        banner.RequestForcePushCommand.Execute(null);
        Assert.True(banner.Replace.IsOpen);
        Assert.Equal("Trag replaces it only while it is still at " + published[..7] + ", where your last fetch saw it. If someone pushed since, nothing is sent.",
            banner.Replace.Text);
        await banner.Replace.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(banner.Replace.IsOpen, banner.Replace.Message);
        Assert.Equal(amended, await journey.Git(hub, "rev-parse", "refs/heads/main"));
        Assert.Equal(amended, await journey.Git(root, "rev-parse", "refs/remotes/origin/main"));
        Assert.False(banner.Summary.Shows);
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        await journey.Git(hub, "fsck", "--no-progress");

        // The second clone, still on the replaced commit, pulls in Trag: nothing is merged, so the commit is not doubled.
        await shell.Session.OpenRepositoryAsync(peer);
        await shell.Sync.SyncCompletion;
        Assert.Equal(peer, shell.Repository.Snapshot!.Root.Replace('/', Path.DirectorySeparatorChar));
        await shell.Sync.PullCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(SyncOutcomeText.FetchedUpstreamRewritten, shell.StatusBar.Status);
        Assert.Equal(published, await journey.Git(peer, "rev-parse", "HEAD"));
        Assert.Equal(amended, await journey.Git(peer, "rev-parse", "refs/remotes/origin/main"));
        Assert.Equal("1", await journey.Git(peer, "rev-list", "--count", "--grep=featrue", "--grep=feature", "HEAD"));
        Assert.Equal("", await journey.Git(peer, "--no-optional-locks", "status", "--porcelain"));
        await journey.Git(peer, "fsck", "--no-progress");
    }
}
