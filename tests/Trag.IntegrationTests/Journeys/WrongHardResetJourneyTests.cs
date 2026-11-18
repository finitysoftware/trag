using Trag.ViewModels.Shell;

namespace Trag.IntegrationTests.Journeys;

// A hard reset to the wrong commit and the way back Trag offers, over real Git. Trag has no reflog view: History
// over all branches still shows the old tip while a ref holds it (here the pushed remote-tracking branch), and a commit
// only the reflog holds is gone from it.
public sealed class WrongHardResetJourneyTests
{
    [Fact]
    public async Task WhenABranchIsHardResetTooFar_ShouldStillShowAndGoBackToATipARefHoldsButNotOneOnlyTheReflogHolds()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        var hub = Path.Combine(fixture.Container, "hub.git");
        await journey.Git(root, "checkout", "-q", "-b", "main");
        await fixture.WriteAsync("plan.txt", "1\n");
        var start = await fixture.CommitAsync("Start the plan");
        await fixture.WriteAsync("plan.txt", "1\n2\n");
        await fixture.CommitAsync("Plan step two");
        await fixture.WriteAsync("plan.txt", "1\n2\n3\n");
        var pushed = await fixture.CommitAsync("Plan step three");
        await journey.Git(fixture.Container, "init", "-q", "--bare", "hub.git");
        await journey.Git(root, "remote", "add", "origin", hub);
        await journey.Git(root, "push", "-q", "-u", "origin", "main");
        await fixture.WriteAsync("plan.txt", "1\n2\n3\n4\n");
        var local = await fixture.CommitAsync("Plan step four");
        await shell.Session.OpenRepositoryAsync(root);
        await shell.Sync.SyncCompletion;

        // History → Reset to the first commit, hard by mistake: the question warns that pushed commits leave the branch.
        await ShowHistoryAsync(shell);
        Assert.True(shell.History.HistoryAllBranches);
        shell.History.SelectedCommit = shell.History.Page.Commits.Single(commit => commit.Id == start);
        await shell.Reset.RequestResetCommand.ExecuteAsync(null);
        Assert.True(shell.Reset.Question.IsOpen);
        Assert.NotEqual("", shell.Reset.PushedWarning);
        shell.Reset.IsHard = true;
        await shell.Reset.Question.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Reset.Question.IsOpen, shell.Reset.Question.Message);
        Assert.Equal(start, await journey.Git(root, "rev-parse", "refs/heads/main"));
        Assert.Equal("refs/heads/main", await journey.Git(root, "symbolic-ref", "HEAD"));
        Assert.Equal("1\n", await File.ReadAllTextAsync(Path.Combine(root, "plan.txt"), fixture.Token));
        Assert.Equal(pushed, await journey.Git(root, "rev-parse", "refs/remotes/origin/main"));
        var reflog = (await journey.Git(root, "reflog", "--format=%H", "refs/heads/main")).Split('\n');
        Assert.Contains(local, reflog);

        // History over all branches: the pushed tip is there, held by origin/main; the local-only tip is not.
        await ShowHistoryAsync(shell);
        var shown = shell.History.Page.Commits.Select(commit => commit.Id).ToArray();
        Assert.Contains(pushed, shown);
        Assert.DoesNotContain(local, shown);

        // Go to commit on the pushed tip: a new branch there, switched to, holds the work again.
        shell.History.SelectedCommit = shell.History.Page.Commits.Single(commit => commit.Id == pushed);
        await shell.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(shell.GoTo.Target.IsOpen, shell.StatusBar.Status);
        Assert.Null(shell.GoTo.Dialog.Tip);
        shell.GoTo.Target.Name = "plan-restored";
        await shell.GoTo.CreateBranchAtCommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.GoTo.Target.IsOpen, shell.GoTo.Target.Notice);
        Assert.Equal("refs/heads/plan-restored", await journey.Git(root, "symbolic-ref", "HEAD"));
        Assert.Equal(pushed, await journey.Git(root, "rev-parse", "HEAD"));
        Assert.Equal("1\n2\n3\n", await File.ReadAllTextAsync(Path.Combine(root, "plan.txt"), fixture.Token));
        Assert.Equal("", await journey.Git(root, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal(start, await journey.Git(root, "rev-parse", "refs/heads/main"));
        await journey.Git(root, "fsck", "--no-progress");
    }

    private static async Task ShowHistoryAsync(ShellViewModel shell)
    {
        shell.Navigator.ShowHistory();
        await shell.History.HistoryCompletion;
    }
}
