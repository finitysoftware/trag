using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// A look around on a detached HEAD that grows commits of its own, which are kept as a branch on one move away and
// left behind on another, over real Git.
public sealed class DetachedRescueJourneyTests
{
    [Fact]
    public async Task WhenCommitsMadeOnADetachedHeadAreKeptOrDiscarded_ShouldHoldThemOnTheNewBranchOrInTheReflog()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var root = journey.Fixture.Root;
        await journey.Git(root, "checkout", "-q", "-b", "main");
        await journey.Fixture.WriteAsync("notes.txt", "one\n");
        var older = await journey.Fixture.CommitAsync("Start the notes");
        await journey.Fixture.WriteAsync("notes.txt", "one\ntwo\n");
        var tip = await journey.Fixture.CommitAsync("Add a second note");
        await shell.Session.OpenRepositoryAsync(root);
        await shell.Sync.SyncCompletion;

        // Go to commit: the older commit of main, looked at on a detached HEAD.
        await GoToAsync(shell, older);
        Assert.Null(shell.GoTo.Dialog.Tip);
        await shell.GoTo.DetachAtCommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.GoTo.Target.IsOpen);
        Assert.Equal(older, await journey.Git(root, "rev-parse", "HEAD"));
        Assert.NotEqual(0, (await journey.Fixture.RunAsync("symbolic-ref", "-q", "HEAD")).ExitCode);
        Assert.Equal(tip, await journey.Git(root, "rev-parse", "refs/heads/main"));
        Assert.Equal("Not on a branch", shell.GoTo.DetachedCardText);

        // Two commits made while looking around.
        var first = await CommitAsync(journey, "notes.txt", "one\nthree\n", "Try a third note");
        var second = await CommitAsync(journey, "notes.txt", "one\nthree\nfour\n", "Try a fourth note");
        Assert.Equal(first, await journey.Git(root, "rev-parse", "HEAD^"));
        Assert.Equal("2 commits are not on a branch", shell.GoTo.DetachedCardText);

        // Double-click on the first of them: the move would leave the second behind, so Keep asks first and keeps both.
        await GoToAsync(shell, first);
        await shell.GoTo.DetachAtCommitCommand.ExecuteAsync(null);
        Assert.True(shell.GoTo.Keep.IsOpen);
        Assert.Equal("Keep 2 commits?", shell.GoTo.KeepCommitsTitle);
        Assert.Equal(second, await journey.Git(root, "rev-parse", "HEAD"));
        var kept = shell.GoTo.Keep.Name;
        Assert.Equal("kept-from-" + second[..7], kept);
        await shell.GoTo.KeepCommitsCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.GoTo.Keep.IsOpen, shell.GoTo.Keep.Notice);
        Assert.Equal(second, await journey.Git(root, "rev-parse", "refs/heads/" + kept));
        Assert.Equal(first, await journey.Git(root, "rev-parse", "refs/heads/" + kept + "^"));
        Assert.Equal(first, await journey.Git(root, "rev-parse", "HEAD"));
        Assert.NotEqual(0, (await journey.Fixture.RunAsync("symbolic-ref", "-q", "HEAD")).ExitCode);
        Assert.StartsWith(BranchText.Kept(kept, ""), shell.StatusBar.Status);
        Assert.Equal("Not on a branch", shell.GoTo.DetachedCardText);

        // Two more commits there, then a double-click on main's tip, whose switch leaves them behind on Discard.
        var third = await CommitAsync(journey, "notes.txt", "one\nthree\nfive\n", "Try a fifth note");
        var fourth = await CommitAsync(journey, "notes.txt", "one\nthree\nfive\nsix\n", "Try a sixth note");
        await GoToAsync(shell, tip);
        Assert.Equal("main", shell.GoTo.Dialog.Tip!.Name);
        await shell.GoTo.SwitchToTipCommand.ExecuteAsync(null);
        Assert.True(shell.GoTo.Keep.IsOpen);
        Assert.Equal("Keep 2 commits?", shell.GoTo.KeepCommitsTitle);
        await shell.GoTo.DiscardCommitsCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.GoTo.Keep.IsOpen, shell.GoTo.Keep.Notice);
        Assert.Equal("refs/heads/main", await journey.Git(root, "symbolic-ref", "HEAD"));
        Assert.Equal(tip, await journey.Git(root, "rev-parse", "HEAD"));
        Assert.Equal("main", shell.Header.BranchText);
        Assert.Equal("", await journey.Git(root, "branch", "--contains", third));
        var reflog = (await journey.Git(root, "reflog", "--format=%H")).Split('\n');
        Assert.Contains(third, reflog);
        Assert.Contains(fourth, reflog);
        Assert.Equal("", await journey.Git(root, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal("one\ntwo\n", await File.ReadAllTextAsync(Path.Combine(root, "notes.txt"), journey.Fixture.Token));
        await journey.Git(root, "fsck", "--no-progress");
    }

    // History → double-click on the commit: the dialog opens for it.
    private static async Task GoToAsync(ShellViewModel shell, string id)
    {
        shell.Navigator.ShowHistory();
        await shell.History.HistoryCompletion;
        shell.History.SelectedCommit = Assert.Single(shell.History.Page.Commits, commit => commit.Id == id);
        await shell.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(shell.GoTo.Target.IsOpen, shell.StatusBar.Status);
        Assert.Equal(id, shell.GoTo.Dialog.Commit!.Id);
    }

    // File status: the edited file staged and committed; Git's new HEAD.
    private static async Task<string> CommitAsync(JourneyShell journey, string path, string content, string message)
    {
        var shell = journey.Shell;
        shell.Navigator.ShowFileStatus();
        await File.WriteAllTextAsync(Path.Combine(journey.Fixture.Root, path), content, journey.Fixture.Token);
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the edited " + path);
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        shell.Commit.CommitMessage = message;
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(message, await journey.Git(journey.Fixture.Root, "log", "-1", "--format=%s"));
        Assert.Equal("", await journey.Git(journey.Fixture.Root, "--no-optional-locks", "status", "--porcelain"));
        return await journey.Git(journey.Fixture.Root, "rev-parse", "HEAD");
    }
}
