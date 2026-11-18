using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Journeys;

// The feature branch journey while a second clone pushes to the feature branch after it was published. The lease
// keeps that commit: the force push sends nothing, a fetch shows the commit and takes the question down, and the rebase
// starts again from the fetched branch, which the force push then replaces with the colleague's commit kept.
public sealed class FeatureBranchLeaseJourneyTests
{
    [Fact]
    public async Task WhenAnotherClonePushesToTheFeatureBranchBeforeItsForcePush_ShouldSendNothingUntilAFetchAndANewRebaseKeepItsCommit()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var (work, hub, start, mainTip) = await FeatureBranch.OpenMainAsync(journey);
        await FeatureBranch.CreateFromTheFirstCommitAsync(journey, start);
        await File.WriteAllTextAsync(Path.Combine(work, "app.txt"), FeatureBranch.App("line 2 on feature", "line 11 on feature"), journey.Fixture.Token);
        await FeatureBranch.StageTheChangedFileAsync(journey, "app.txt");
        await FeatureBranch.CommitAsync(journey, "Change the edges");
        await FeatureBranch.CommitNotesAsync(journey);
        await FeatureBranch.PublishAsync(journey);
        var published = await journey.Git(work, "rev-parse", "HEAD");

        // A colleague's clone adds a commit to the feature branch on the remote.
        var colleague = Path.Combine(journey.Fixture.Container, "colleague");
        await journey.Git(journey.Fixture.Container, "clone", "--branch", "feature", hub, colleague);
        await File.WriteAllTextAsync(Path.Combine(colleague, "colleague.txt"), "From a colleague.\n", journey.Fixture.Token);
        await journey.Git(colleague, "add", "colleague.txt");
        await journey.Git(colleague, "-c", "user.name=Colleague", "-c", "user.email=colleague@example.invalid", "commit", "-m", "Add the colleague's note");
        await journey.Git(colleague, "push", "origin", "feature");
        var theirs = await journey.Git(colleague, "rev-parse", "HEAD");
        Assert.Equal(published, await journey.Git(colleague, "rev-parse", "HEAD^"));

        // Trag has not fetched: the rebase onto main makes the branch look rewritten after its push, and the force push
        // with lease finds the remote branch elsewhere than the last fetch saw it, sends nothing and says to fetch.
        await FeatureBranch.RebaseOntoMainAsync(journey, mainTip);
        var force = shell.FileStatusPage.ForcePush;
        Assert.True(force.Summary.Shows);
        force.RequestForcePushCommand.Execute(null);
        await force.Replace.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.True(force.Replace.IsOpen);
        Assert.Equal(SyncOutcomeText.RemoteMoved, force.Replace.Message);
        Assert.Equal(theirs, await journey.Git(hub, "rev-parse", "refs/heads/feature"));
        Assert.Equal(published, await journey.Git(work, "rev-parse", "refs/remotes/origin/feature"));

        // The fetch brings the colleague's commit: the branch is no longer only rewritten, so the banner and its question go.
        await shell.Sync.FetchCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(theirs, await journey.Git(work, "rev-parse", "refs/remotes/origin/feature"));
        Assert.False(force.Summary.Shows);
        Assert.False(force.Replace.IsOpen);
        Assert.Equal((4, 4), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.Equal(theirs, await journey.Git(hub, "rev-parse", "refs/heads/feature"));

        // The rebase again, first onto the fetched branch: Git leaves out the two commits it already has, stops at main's
        // commit, which the feature does not take, and drops the first one, which the colleague's branch already holds.
        await shell.MergeDialog.OpenCommand.ExecuteAsync(null);
        await shell.MergeDialog.ShowRebaseCommand.ExecuteAsync(null);
        var rebase = shell.MergeDialog.Rebase;
        rebase.Draft.Onto = rebase.Draft.OntoOptions.Single(row => row.Name == "origin/feature");
        await ShellHarness.Until(() => rebase.Summary.ShowsResult, "the rebase preview");
        Assert.Equal("Git leaves out 2 commits origin/feature already has.", rebase.Summary.LeftOutText);
        await rebase.RebaseCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.MergeDialog.IsOpen, rebase.Draft.Notice);
        Assert.Equal("Change the start on main", shell.Rebase.Message);
        Assert.Equal("UU app.txt", await journey.Git(work, "status", "--porcelain"));
        await shell.Rebase.SkipCommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(Directory.Exists(await journey.Git(work, "rev-parse", "--git-path", "rebase-merge")));
        Assert.Equal(theirs, await journey.Git(work, "rev-parse", "feature"));
        Assert.Equal("", await journey.Git(work, "status", "--porcelain"));
        Assert.False(force.Summary.Shows);

        // Then onto main as before, which the force push with the lease of the fetched tip sends.
        await FeatureBranch.RebaseOntoMainAsync(journey, mainTip);
        Assert.Equal("Add the colleague's note\nExtend notes\nAdd notes\nChange the edges", await journey.Git(work, "log", "--format=%s", "main..feature"));

        await FeatureBranch.ForcePushAsync(journey);
        Assert.Equal("From a colleague.", await journey.Git(hub, "show", "refs/heads/feature:colleague.txt"));
        Assert.Equal(mainTip, await journey.Git(hub, "merge-base", "refs/heads/main", "refs/heads/feature"));
        await journey.Git(work, "fsck", "--no-progress");
        await journey.Git(hub, "fsck", "--no-progress");
    }
}
