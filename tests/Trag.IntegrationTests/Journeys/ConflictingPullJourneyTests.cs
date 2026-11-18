using Trag.Domain.Presentation;
using Trag.TestSupport;
using Trag.ViewModels.Merge;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// Two machines change the same file, and the one with Trag pulls into a conflict, resolves it block by block and
// pushes the merge, which the other machine then pulls fast-forward; over real Git.
public sealed class ConflictingPullJourneyTests
{
    // Unchanged lines enough to keep Git from joining neighbouring conflicts into one block.
    private const string Gap = "line a\nline b\nline c\nline d\nline e\nline f\n";
    internal const string Base = "header\nfirst = base\n" + Gap + "second = base\n" + Gap + "third = base\nfooter\n";

    [Fact]
    public async Task WhenTwoClonesChangeTheSameFile_ShouldPullIntoAConflictResolveItBlockByBlockAndLeaveBothClonesWithTheSameFile()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var (mine, other, hub) = await TwoMachinesAsync(journey);

        await File.WriteAllTextAsync(Path.Combine(other, "notes.txt"), Base.Replace("= base", "= theirs"), journey.Fixture.Token);
        await journey.Git(other, "commit", "-qam", "Change the notes over there");
        await journey.Git(other, "push", "-q", "origin", "main");
        var theirs = await journey.Git(other, "rev-parse", "HEAD");

        // This machine changes the same lines and commits through Trag.
        await File.WriteAllTextAsync(Path.Combine(mine, "notes.txt"), Base.Replace("= base", "= mine"), journey.Fixture.Token);
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the edited notes.txt");
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        shell.Commit.CommitMessage = "Change the notes here";
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var ours = await journey.Git(mine, "rev-parse", "HEAD");
        Assert.Equal("Change the notes here", await journey.Git(mine, "log", "-1", "--format=%s"));

        // Fetch: one ahead, one behind.
        await shell.Sync.FetchCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(theirs, await journey.Git(mine, "rev-parse", "refs/remotes/origin/main"));
        Assert.Equal("1\t1", await journey.Git(mine, "rev-list", "--left-right", "--count", "HEAD...@{upstream}"));
        Assert.Equal((1, 1), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.Equal("↑ 1 ahead  ·  ↓ 1 behind", shell.Sync.Panel.Counts.Text);

        // Pull: the merge stops on the file, and the merge view shows its three blocks.
        await shell.Sync.PullCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(theirs, await journey.Git(mine, "rev-parse", "MERGE_HEAD"));
        Assert.Equal(ours, await journey.Git(mine, "rev-parse", "HEAD"));
        Assert.Equal("UU notes.txt", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal(WorkspaceSection.Merge, shell.Navigator.Section);
        Assert.Equal("origin/main → main", shell.Merge.Overview.Direction);
        Assert.Equal("1 of 1 file is still in conflict.", shell.Merge.Overview.Remaining);
        Assert.Equal("notes.txt", shell.Resolution.SelectedConflict!.PathText);
        await Blocks(shell, 3);

        // Theirs for the first block, yours for the second, both for the third.
        await shell.ConflictsPage.Conflict.TakeOtherCommand.ExecuteAsync(await Blocks(shell, 3, 0));
        Assert.Equal("UU notes.txt", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));
        await shell.ConflictsPage.Conflict.TakeYoursCommand.ExecuteAsync(await Blocks(shell, 2, 0));
        await shell.ConflictsPage.Conflict.TakeBothCommand.ExecuteAsync(await Blocks(shell, 1, 0));
        await Blocks(shell, 0);
        var resolved = "header\nfirst = theirs\n" + Gap + "second = mine\n" + Gap + "third = mine\nthird = theirs\nfooter\n";
        Assert.Equal(resolved, await File.ReadAllTextAsync(Path.Combine(mine, "notes.txt"), journey.Fixture.Token));
        Assert.Equal("UU notes.txt", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));

        // Mark resolved, then Finish with Git's message: a merge commit of both sides.
        Assert.True(shell.Resolution.StageResolutionCommand.CanExecute(null));
        await shell.Resolution.StageResolutionCommand.ExecuteAsync(null);
        Assert.Equal("M  notes.txt", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal("All conflicts are resolved. Review the result and finish.", shell.Merge.Overview.Remaining);
        Assert.True(shell.Merge.FinishMergeCommand.CanExecute(null));
        await shell.Merge.FinishMergeCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var merge = await journey.Git(mine, "rev-parse", "HEAD");
        Assert.Equal(ours + " " + theirs, await journey.Git(mine, "log", "-1", "--format=%P"));
        Assert.Equal("Merge remote-tracking branch 'origin/main' into main", await journey.Git(mine, "log", "-1", "--format=%B"));
        Assert.NotEqual(0, (await journey.Fixture.RunAtAsync(mine, "rev-parse", "--verify", "--quiet", "MERGE_HEAD")).ExitCode);
        Assert.Equal("", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal(WorkspaceSection.FileStatus, shell.Navigator.Section);
        Assert.Equal((2, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));

        // Push, and the other machine pulls fast-forward to the same file.
        await shell.Sync.PushCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(merge, await journey.Git(hub, "rev-parse", "refs/heads/main"));
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        await journey.Git(other, "pull", "-q", "--ff-only");
        Assert.Equal(merge, await journey.Git(other, "rev-parse", "HEAD"));
        Assert.Equal(resolved, await File.ReadAllTextAsync(Path.Combine(other, "notes.txt"), journey.Fixture.Token));
        Assert.Equal(await journey.Git(mine, "rev-parse", "HEAD:notes.txt"), await journey.Git(other, "hash-object", "notes.txt"));
        Assert.Equal("", await journey.Git(other, "--no-optional-locks", "status", "--porcelain"));
        foreach (var repository in new[] { mine, other, hub }) await journey.Git(repository, "fsck", "--no-progress");
    }

    // A bare hub with one commit of notes.txt on main and two clones of it, the one this machine opens in Trag and the
    // other machine's; each clone has the identity a user's machine has.
    internal static async Task<(string Mine, string Other, string Hub)> TwoMachinesAsync(JourneyShell journey)
    {
        var container = journey.Fixture.Container;
        var (mine, other, hub) = (Path.Combine(container, "this machine"), Path.Combine(container, "other machine"), Path.Combine(container, "hub.git"));
        var seed = journey.Fixture.Root;
        await journey.Git(container, "init", "-q", "--bare", "--initial-branch=main", "hub.git");
        await File.WriteAllTextAsync(Path.Combine(seed, "notes.txt"), Base, journey.Fixture.Token);
        await journey.Fixture.CommitAsync("Start the notes", "notes.txt");
        await journey.Git(seed, "push", "-q", hub, "HEAD:refs/heads/main");
        foreach (var clone in new[] { mine, other })
        {
            await journey.Git(container, "clone", "-q", hub, clone);
            await journey.Git(clone, "config", "user.name", "Journey User");
            await journey.Git(clone, "config", "user.email", "journey@example.invalid");
        }

        journey.Host.Folder = mine;
        await journey.Shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Equal("this machine", journey.Shell.Catalog.State.SelectedLocal!.Name);
        await journey.Shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await journey.Shell.Sync.SyncCompletion;
        Assert.Equal("main", journey.Shell.Repository.Snapshot!.Branch);
        return (mine, other, hub);
    }

    // The conflict blocks the merge view shows, once it shows as many as expected; the one asked for is returned. A read of
    // the file that the watcher's read after a write overtook keeps the blocks before it, and the watcher's snapshot reads
    // the file again, so the count is waited for.
    internal static async Task<ConflictPartRow?> Blocks(ShellViewModel shell, int count, int? index = null)
    {
        var conflict = shell.ConflictsPage.Conflict;
        await ShellHarness.Until(() => !shell.Repository.IsBusy && !conflict.Document.IsBusy && conflict.Parts.Count(part => part.Part.IsBlock) == count,
            count + " conflict blocks in the merge view");
        var blocks = conflict.Parts.Where(part => part.Part.IsBlock).ToArray();
        return index is { } at ? blocks[at] : null;
    }
}
