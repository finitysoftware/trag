using System.Text;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// An interactive rebase a terminal starts while Trag is open, stopped for an edit, which the user finishes in Trag.
public sealed class TerminalRebaseJourneyTests
{
    [Fact]
    public async Task WhenATerminalsInteractiveRebaseStopsForAnEdit_ShouldShowItAndLetTragFinishItForTheTerminal()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        var branch = await journey.Git(root, "symbolic-ref", "--short", "HEAD");
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.WriteAsync("b.txt", "b\n");
        var start = await fixture.CommitAsync("Start");
        await fixture.WriteAsync("a.txt", "a\nfirst\n");
        await fixture.CommitAsync("Change a", "a.txt");
        await fixture.WriteAsync("b.txt", "b\nsecond\n");
        await fixture.CommitAsync("Change b", "b.txt");

        // The repository is open in Trag, showing the file status.
        journey.Host.Folder = root;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(WorkspaceSection.FileStatus, shell.Navigator.Section);
        Assert.Null(shell.CurrentSection);

        // The terminal: git rebase -i, its todo written by the sequence editor, which turns the first pick into an edit.
        var terminal = await fixture.Runner.RunAsync(root, ["rebase", "-i", start], fixture.Token,
            new Dictionary<string, string?> { ["GIT_SEQUENCE_EDITOR"] = "sed -i 1s/^[a-z]*/edit/" });
        Assert.True(terminal.ExitCode == 0, Encoding.UTF8.GetString(terminal.StandardError));
        Assert.Equal("Change a", await journey.Git(root, "log", "-1", "--format=%s"));
        Assert.True(Directory.Exists(Path.Combine(root, ".git", "rebase-merge")));

        // Trag sees the rebase in progress on its own and shows the rebase view at the stop.
        await ShellHarness.Until(() => shell.Navigator.Section == WorkspaceSection.Rebase && !shell.Repository.IsBusy, "the watcher to show the terminal's rebase");
        Assert.Equal(RepositoryOperation.Rebase, shell.Repository.Snapshot!.Operation);
        Assert.IsType<RebasePage>(shell.CurrentSection);
        Assert.Equal("commit 1 of 2 stopped so you can edit it. Continue replays the rest.", shell.Rebase.Overview.Remaining);
        Assert.Equal("Change a", shell.Rebase.Message.TrimEnd());

        // The user changes a file at the stop and stages it in Trag.
        await fixture.WriteAsync("a.txt", "a\nfirst, edited at the stop\n");
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the edited a.txt");
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal("a.txt", await journey.Git(root, "diff", "--cached", "--name-only"));
        Assert.True(Directory.Exists(Path.Combine(root, ".git", "rebase-merge")));

        // Continue from Trag finishes the rebase the terminal started.
        Assert.True(shell.Rebase.ContinueRebaseCommand.CanExecute(null));
        await shell.Rebase.ContinueRebaseCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var written = shell.Repository.Writer.LastWrite!;
        Assert.True(written.Result is { Outcome: GitWriteOutcome.Succeeded }, written.Result?.Message + written.Result?.Details);
        Assert.Equal(WorkspaceSection.FileStatus, shell.Navigator.Section);
        Assert.Equal(RepositoryOperation.None, shell.Repository.Snapshot!.Operation);

        // The terminal sees the rebase finished: the branch checked out again, both commits on it, the edit in the first.
        Assert.False(Directory.Exists(Path.Combine(root, ".git", "rebase-merge")));
        Assert.Equal(branch, await journey.Git(root, "symbolic-ref", "--short", "HEAD"));
        Assert.Equal("Change b\nChange a\nStart", await journey.Git(root, "log", "--format=%s"));
        Assert.Equal("a\nfirst, edited at the stop", await journey.Git(root, "show", "HEAD~1:a.txt"));
        Assert.Equal("b\nsecond", await journey.Git(root, "show", "HEAD:b.txt"));
        Assert.Equal(start, await journey.Git(root, "rev-parse", "HEAD~2"));
        Assert.Equal("", await journey.Git(root, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal(await journey.Git(root, "rev-parse", "HEAD"), shell.Repository.Snapshot.Head);
        var again = await fixture.RunAsync("rebase", "--continue");
        Assert.NotEqual(0, again.ExitCode);
        await journey.Git(root, "fsck", "--no-progress");
    }
}
