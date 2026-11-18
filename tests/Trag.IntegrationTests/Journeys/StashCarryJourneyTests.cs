using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Journeys;

// Work stashed with its untracked files, carried to another branch and applied there over a conflict, which is
// resolved in the Conflicts view; the stash stays until the user drops it, over real Git.
public sealed class StashCarryJourneyTests
{
    [Fact]
    public async Task WhenAStashWithUntrackedFilesIsAppliedOnAnotherBranchOverAConflict_ShouldKeepEveryFileAndTheStashUntilDropped()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        await journey.Git(root, "checkout", "-q", "-b", "main");
        await journey.Git(root, "config", "core.autocrlf", "false");
        await fixture.WriteAsync("f.txt", "a\nb\nc\n");
        await fixture.WriteAsync("g.txt", "x\n");
        await fixture.CommitAsync("Base");
        await journey.Git(root, "checkout", "-q", "-b", "other");
        await fixture.WriteAsync("f.txt", "a\nB-other\nc\n");
        await fixture.WriteAsync("g.txt", "x-other\n");
        var other = await fixture.CommitAsync("Other");
        await journey.Git(root, "checkout", "-q", "main");
        var main = await journey.Git(root, "rev-parse", "HEAD");
        await fixture.WriteAsync("f.txt", "a\nB-mine\nc\n");
        await fixture.WriteAsync("g.txt", "x-mine\n");
        await fixture.WriteAsync("new.txt", "new work\n");
        await fixture.WriteAsync("notes/deep.txt", "deep work\n");
        await shell.Session.OpenRepositoryAsync(root);
        await shell.Sync.SyncCompletion;
        Assert.Equal(4, shell.FileStatus.UnstagedFiles.Count);

        // Stash with "Include untracked files": the tree is clean and the untracked files are in the stash.
        shell.Stash.OpenStashCommand.Execute(null);
        shell.Stash.Draft.Message = "Work in progress";
        shell.Stash.Draft.IncludeUntracked = true;
        await shell.Stash.CreateStashCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Stash.Draft.IsOpen, shell.Stash.Draft.Notice);
        Assert.Equal("", await journey.Git(root, "--no-optional-locks", "status", "--porcelain", "--untracked-files=all"));
        Assert.Equal("On main: Work in progress", await journey.Git(root, "stash", "list", "--format=%gs"));
        Assert.Equal("new.txt\nnotes/deep.txt", await journey.Git(root, "ls-tree", "-r", "--name-only", "stash@{0}^3"));
        Assert.Equal(main, await journey.Git(root, "rev-parse", "HEAD"));

        // Switch to the other branch from the branch picker.
        await shell.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        await shell.Branches.SwitchToBranchCommand.ExecuteAsync(shell.Branches.Rows.Local.Single(row => row.Name == "other"));
        await shell.Sync.SyncCompletion;
        Assert.Equal("refs/heads/other", await journey.Git(root, "symbolic-ref", "HEAD"));
        Assert.Equal(other, await journey.Git(root, "rev-parse", "HEAD"));

        // Apply: both tracked files conflict, the untracked files come back.
        await shell.Stash.OpenStashPickerCommand.ExecuteAsync(null);
        await shell.Stash.ApplyCommand.ExecuteAsync(Assert.Single(shell.Stash.Rows));
        await shell.Sync.SyncCompletion;
        Assert.Equal(RepositoryOperation.Conflict, shell.Repository.Snapshot!.Operation);
        Assert.Equal("f.txt\ng.txt", await journey.Git(root, "diff", "--name-only", "--diff-filter=U"));
        Assert.Equal("new work\n", await File.ReadAllTextAsync(Path.Combine(root, "new.txt"), fixture.Token));
        Assert.Equal("deep work\n", await File.ReadAllTextAsync(Path.Combine(root, "notes", "deep.txt"), fixture.Token));
        Assert.Equal("1", await journey.Git(root, "rev-list", "--walk-reflogs", "--count", "refs/stash"));

        // The Conflicts view: g.txt takes the stash's side, f.txt is edited by hand and marked resolved.
        shell.Navigator.ShowConflictsCommand.Execute(null);
        Assert.Same(shell.ConflictsPage, shell.CurrentSection);
        shell.Resolution.SelectedConflict = shell.Resolution.Conflicts.Single(row => row.File.Path.Display == "g.txt");
        await shell.Resolution.UseYoursCommand.ExecuteAsync(null);
        Assert.Equal("x-mine\n", await File.ReadAllTextAsync(Path.Combine(root, "g.txt"), fixture.Token));
        await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "a\nB-other\nB-mine\nc\n", fixture.Token);
        await ShellHarness.Until(() => shell.Resolution.Conflicts is [{ CanMarkResolved: true }] && !shell.Repository.IsBusy, "the watcher to read the edited f.txt");
        shell.Resolution.SelectedConflict = shell.Resolution.Conflicts[0];
        await shell.Resolution.StageResolutionCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Empty(shell.Resolution.Conflicts);
        Assert.Equal(RepositoryOperation.None, shell.Repository.Snapshot!.Operation);
        Assert.Equal("", await journey.Git(root, "diff", "--name-only", "--diff-filter=U"));
        Assert.Equal("M  f.txt\nM  g.txt\n?? new.txt\n?? notes/deep.txt",
            await journey.Git(root, "--no-optional-locks", "status", "--porcelain", "--untracked-files=all"));
        Assert.Equal("a\nB-other\nB-mine\nc", await journey.Git(root, "show", ":f.txt"));
        Assert.Equal(other, await journey.Git(root, "rev-parse", "HEAD"));

        // The stash stays, in Git and in the picker, until the user drops it.
        await shell.Stash.OpenStashPickerCommand.ExecuteAsync(null);
        var row = Assert.Single(shell.Stash.Rows);
        Assert.Equal("1", await journey.Git(root, "rev-list", "--walk-reflogs", "--count", "refs/stash"));
        shell.Stash.RequestDropCommand.Execute(row);
        await shell.Stash.Drop.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Stash.Drop.IsOpen, shell.Stash.Drop.Message);
        Assert.NotEqual(0, (await fixture.RunAsync("rev-parse", "--verify", "--quiet", "refs/stash")).ExitCode);
        Assert.Empty(shell.Stash.Rows);
        Assert.Equal("new work\n", await File.ReadAllTextAsync(Path.Combine(root, "new.txt"), fixture.Token));
        Assert.Equal("deep work\n", await File.ReadAllTextAsync(Path.Combine(root, "notes", "deep.txt"), fixture.Token));
        await journey.Git(root, "fsck", "--no-progress");
    }
}
