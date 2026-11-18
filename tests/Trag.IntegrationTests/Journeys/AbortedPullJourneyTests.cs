using Trag.Domain.Presentation;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// A pull that stopped on a conflict outlasts the stash dialog, the history and a restart of the application, and
// Abort then puts the branch back where it was before the pull, leaving a file made meanwhile and not tracked untouched; over
// real Git.
public sealed class AbortedPullJourneyTests
{
    [Fact]
    public async Task WhenAConflictedPullOutlastsTheStashDialogTheHistoryAndARestart_ShouldAbortToTheStateBeforeThePull()
    {
        await using var journey = await JourneyShell.StartAsync();
        var (mine, other, _) = await ConflictingPullJourneyTests.TwoMachinesAsync(journey);
        var token = journey.Fixture.Token;
        var notes = Path.Combine(mine, "notes.txt");
        await File.WriteAllTextAsync(Path.Combine(other, "notes.txt"), ConflictingPullJourneyTests.Base.Replace("= base", "= theirs"), token);
        await journey.Git(other, "commit", "-qam", "Change the notes over there");
        await journey.Git(other, "push", "-q", "origin", "main");
        var theirs = await journey.Git(other, "rev-parse", "HEAD");
        var mineText = ConflictingPullJourneyTests.Base.Replace("= base", "= mine");
        await File.WriteAllTextAsync(notes, mineText, token);
        await journey.Git(mine, "commit", "-qam", "Change the notes here");
        var ours = await journey.Git(mine, "rev-parse", "HEAD");
        var shell = journey.Shell;
        await ShellHarness.Until(() => shell.Repository.Snapshot!.Head == ours && !shell.Repository.IsBusy, "the watcher to show the commit");
        await shell.Sync.SyncCompletion;

        // Pull into the conflict, and take one block, which Abort has to take back as well.
        await shell.Sync.PullCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(theirs, await journey.Git(mine, "rev-parse", "MERGE_HEAD"));
        Assert.Equal(ours, await journey.Git(mine, "rev-parse", "ORIG_HEAD"));
        Assert.Equal(WorkspaceSection.Merge, shell.Navigator.Section);
        await shell.ConflictsPage.Conflict.TakeOtherCommand.ExecuteAsync(await ConflictingPullJourneyTests.Blocks(shell, 3, 0));
        await ConflictingPullJourneyTests.Blocks(shell, 2);
        Assert.StartsWith("header\nfirst = theirs\n", await File.ReadAllTextAsync(notes, token));

        // A file of the user's own that Git does not track, made while the conflict is open (Pull itself waits for a
        // clean tree, untracked files included).
        await File.WriteAllTextAsync(Path.Combine(mine, "scratch.txt"), "not for Git\n", token);
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the untracked scratch.txt");

        // The stash dialog stays shut while the merge is in progress: stashing now would put the conflict away.
        Assert.False(shell.Stash.OpenStashCommand.CanExecute(null));
        shell.Stash.OpenStashCommand.Execute(null);
        Assert.False(shell.Stash.Draft.IsOpen);
        shell.Stash.CancelStashCommand.Execute(null);
        Assert.Equal("", await journey.Git(mine, "stash", "list"));
        Assert.Equal(theirs, await journey.Git(mine, "rev-parse", "MERGE_HEAD"));

        // The history shows both sides of the merge, and the merge view is where it was.
        shell.Navigator.ShowHistory();
        await ShellHarness.Until(() => !shell.History.IsHistoryBusy && shell.History.Page.Commits.Count != 0, "the history to show");
        Assert.Equal(WorkspaceSection.History, shell.Navigator.Section);
        Assert.Contains(shell.History.Page.Commits, commit => commit.Id == ours);
        Assert.Contains(shell.History.Page.Commits, commit => commit.Id == theirs);
        shell.Navigator.ShowMergeCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Merge, shell.Navigator.Section);
        Assert.Equal("1 of 1 file is still in conflict.", shell.Merge.Overview.Remaining);

        // A restart: the repository opened from the list again shows the merge it left, with the block already taken.
        await journey.RestartAsync();
        shell = journey.Shell;
        Assert.False(shell.Repository.HasRepository);
        shell.Catalog.State.SelectedLocal = Assert.Single(shell.Catalog.Local.LocalRows);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.True(shell.Repository.HasMerge);
        Assert.Equal(WorkspaceSection.Merge, shell.Navigator.Section);
        Assert.Equal("origin/main → main", shell.Merge.Overview.Direction);
        Assert.Equal("1 of 1 file is still in conflict.", shell.Merge.Overview.Remaining);
        Assert.Equal("notes.txt", shell.Resolution.SelectedConflict!.PathText);
        await ConflictingPullJourneyTests.Blocks(shell, 2);

        // Abort: the branch, the index and the file are as before the pull, the fetched commits stay fetched, and the
        // untracked file is still there.
        shell.Merge.RequestAbortMergeCommand.Execute(null);
        Assert.True(shell.Merge.Abort.IsOpen);
        await shell.Merge.Abort.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Merge.Abort.IsOpen);
        Assert.NotEqual(0, (await journey.Fixture.RunAtAsync(mine, "rev-parse", "--verify", "--quiet", "MERGE_HEAD")).ExitCode);
        Assert.Equal(ours, await journey.Git(mine, "rev-parse", "HEAD"));
        Assert.Equal(ours, await journey.Git(mine, "rev-parse", "ORIG_HEAD"));
        Assert.Equal("refs/heads/main", await journey.Git(mine, "symbolic-ref", "HEAD"));
        Assert.Equal("?? scratch.txt", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain", "--untracked-files=all"));
        Assert.Equal(mineText, await File.ReadAllTextAsync(notes, token));
        Assert.Equal("not for Git\n", await File.ReadAllTextAsync(Path.Combine(mine, "scratch.txt"), token));
        Assert.Equal(theirs, await journey.Git(mine, "rev-parse", "refs/remotes/origin/main"));
        Assert.Equal(WorkspaceSection.FileStatus, shell.Navigator.Section);
        Assert.False(shell.Repository.HasMerge);
        Assert.Equal((1, 1), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.Equal("scratch.txt", ((StatusRow)Assert.Single(shell.FileStatus.UnstagedFiles)!).File.Path.Display);
        await journey.Git(mine, "fsck", "--no-progress");
    }
}
