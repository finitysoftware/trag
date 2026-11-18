using Trag.Domain.Presentation;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// The steps the feature branch journeys (6.2) share, each driven as the user takes it and checked in Git afterwards. The
// fixture's repository holds main with two commits, on a bare remote beside it: app.txt, then a change to its second
// line that the feature's own change to that line meets in the rebase.
internal static class FeatureBranch
{
    private static readonly string[] Lines = [.. Enumerable.Range(1, 12).Select(number => "line " + number)];

    // app.txt with its second and eleventh lines as given.
    public static string App(string? second = null, string? eleventh = null) =>
        string.Concat(Lines.Select((line, index) => (index == 1 ? second ?? line : index == 10 ? eleventh ?? line : line) + "\n"));

    // main pushed to the remote, the repository added to the list and opened on it.
    public static async Task<(string Work, string Hub, string Start, string MainTip)> OpenMainAsync(JourneyShell journey)
    {
        var shell = journey.Shell;
        var work = journey.Fixture.Root;
        var hub = Path.Combine(journey.Fixture.Container, "hub.git");
        await journey.Git(work, "symbolic-ref", "HEAD", "refs/heads/main");
        await journey.Fixture.WriteAsync("app.txt", App());
        var start = await journey.Fixture.CommitAsync("Add the app");
        await journey.Fixture.WriteAsync("app.txt", App(second: "line 2 on main"));
        var mainTip = await journey.Fixture.CommitAsync("Change the start on main");
        await journey.Git(journey.Fixture.Container, "init", "--bare", "--initial-branch=main", "hub.git");
        await journey.Git(work, "remote", "add", "origin", hub);
        await journey.Git(work, "push", "--set-upstream", "origin", "main");

        journey.Host.Folder = work;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal("main", shell.Repository.Snapshot!.Branch);
        return (work, hub, start, mainTip);
    }

    // History → the first commit → Create branch from this commit, switching to it; the file status shows it then.
    public static async Task CreateFromTheFirstCommitAsync(JourneyShell journey, string start)
    {
        var shell = journey.Shell;
        shell.Navigator.ShowHistory();
        await shell.History.HistoryCompletion;
        shell.History.SelectedCommit = shell.History.Page.Commits.Single(commit => commit.Id == start);
        await shell.Branches.CreateBranchFromCommitCommand.ExecuteAsync(null);
        Assert.Equal(start, shell.Branches.Draft.SelectedStartPoint!.Ref);
        Assert.True(shell.Branches.Draft.SwitchAfterCreate);
        shell.Branches.Draft.Name = "feature";
        await shell.Branches.CreateBranchCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Branches.Draft.IsOpen, shell.Branches.Draft.Notice);
        Assert.Equal("refs/heads/feature", await journey.Git(journey.Fixture.Root, "symbolic-ref", "HEAD"));
        Assert.Equal(start, await journey.Git(journey.Fixture.Root, "rev-parse", "HEAD"));
        Assert.Equal("feature", shell.Repository.Snapshot!.Branch);
        shell.Navigator.ShowFileStatus();
    }

    public static async Task StageTheChangedFileAsync(JourneyShell journey, string path)
    {
        var shell = journey.Shell;
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show " + path);
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(path, await journey.Git(journey.Fixture.Root, "diff", "--cached", "--name-only"));
    }

    public static async Task CommitAsync(JourneyShell journey, string message)
    {
        var shell = journey.Shell;
        shell.Commit.CommitMessage = message;
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(message, await journey.Git(journey.Fixture.Root, "log", "-n1", "--format=%s"));
        Assert.Equal("", await journey.Git(journey.Fixture.Root, "status", "--porcelain"));
    }

    // The feature's second and third commits: notes.txt added, then extended.
    public static async Task CommitNotesAsync(JourneyShell journey)
    {
        var work = journey.Fixture.Root;
        await File.WriteAllTextAsync(Path.Combine(work, "notes.txt"), "First note.\n", journey.Fixture.Token);
        await StageTheChangedFileAsync(journey, "notes.txt");
        await CommitAsync(journey, "Add notes");
        await File.AppendAllTextAsync(Path.Combine(work, "notes.txt"), "Second note.\n", journey.Fixture.Token);
        await StageTheChangedFileAsync(journey, "notes.txt");
        await CommitAsync(journey, "Extend notes");
        Assert.Equal("Extend notes\nAdd notes\nChange the edges", await journey.Git(work, "log", "--format=%s", "main..feature"));
    }

    // A branch with no upstream is published to the remote the repository has: the dialog offers it with a new branch of
    // the same name there, pushes, and records the upstream.
    public static async Task PublishAsync(JourneyShell journey)
    {
        var shell = journey.Shell;
        var work = journey.Fixture.Root;
        Assert.True(shell.Publish.Context.ShowBranchCard);
        await shell.Publish.OpenPublishCommand.ExecuteAsync(null);
        var draft = shell.Publish.Draft;
        Assert.True(draft.IsBranchMode);
        Assert.Equal(("origin", "feature", true), (draft.ExistingRemote?.Name, draft.Targets.Selected?.Name, draft.Targets.Selected?.Target.IsNew));
        await shell.Publish.PublishCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(draft.IsOpen, draft.Notice + shell.Publish.PublishDetails);
        var head = await journey.Git(work, "rev-parse", "HEAD");
        Assert.Equal(head, await journey.Git(Path.Combine(journey.Fixture.Container, "hub.git"), "rev-parse", "refs/heads/feature"));
        Assert.Equal("origin/feature", await journey.Git(work, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"));
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
    }

    // The merge dialog's rebase side: feature onto main, which stops at the conflict on the second line. The rebase view
    // resolves its one block with the feature's line, shown as yours, marks the file resolved, and Continue replays the rest.
    public static async Task RebaseOntoMainAsync(JourneyShell journey, string mainTip)
    {
        var shell = journey.Shell;
        var work = journey.Fixture.Root;
        await shell.MergeDialog.OpenCommand.ExecuteAsync(null);
        await shell.MergeDialog.ShowRebaseCommand.ExecuteAsync(null);
        var rebase = shell.MergeDialog.Rebase;
        rebase.Draft.Onto = rebase.Draft.OntoOptions.Single(row => row.Name == "main");
        await ShellHarness.Until(() => rebase.Summary.ShowsResult, "the rebase preview");
        Assert.Equal(["app.txt"], rebase.Summary.ConflictFiles);
        Assert.True(rebase.RebaseCommand.CanExecute(null), rebase.Draft.Notice);
        await rebase.RebaseCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.MergeDialog.IsOpen, rebase.Draft.Notice);
        Assert.Equal(WorkspaceSection.Rebase, shell.Navigator.Section);
        Assert.Equal("UU app.txt", await journey.Git(work, "status", "--porcelain"));
        Assert.Equal(mainTip, await journey.Git(work, "rev-parse", "HEAD"));

        shell.Resolution.SelectedConflict = Assert.Single(shell.Resolution.Conflicts);
        await ShellHarness.Until(() => shell.ConflictsPage.Conflict.Parts.Any(row => row.Part.IsBlock), "the conflict block of app.txt");
        var block = shell.ConflictsPage.Conflict.Parts.Single(row => row.Part.IsBlock);
        Assert.Equal(["line 2 on feature"], block.Yours.Select(line => line.Text));
        Assert.Equal(["line 2 on main"], block.Other.Select(line => line.Text));
        await shell.ConflictsPage.Conflict.TakeYoursCommand.ExecuteAsync(block);
        await shell.Sync.SyncCompletion;
        Assert.Equal(App("line 2 on feature", "line 11 on feature"), await File.ReadAllTextAsync(Path.Combine(work, "app.txt"), journey.Fixture.Token));
        await shell.Resolution.StageResolutionCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal("M  app.txt", await journey.Git(work, "status", "--porcelain"));
        Assert.Empty(shell.Resolution.Conflicts);

        Assert.Equal("Change the edges", shell.Rebase.Message);
        await shell.Rebase.ContinueRebaseCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.NotEqual(0, (await journey.Fixture.RunAtAsync(work, "rev-parse", "--verify", "--quiet", "REBASE_HEAD")).ExitCode);
        Assert.False(Directory.Exists(await journey.Git(work, "rev-parse", "--git-path", "rebase-merge")));
        Assert.Equal("refs/heads/feature", await journey.Git(work, "symbolic-ref", "HEAD"));
        Assert.Equal(mainTip, await journey.Git(work, "merge-base", "main", "feature"));
        Assert.Equal(App("line 2 on feature", "line 11 on feature").TrimEnd(), await journey.Git(work, "show", "feature:app.txt"));
        Assert.Equal("", await journey.Git(work, "status", "--porcelain"));
        Assert.NotEqual(WorkspaceSection.Rebase, shell.Navigator.Section);
    }

    // The banner over the file status: Force push with lease, then the question, replaces the remote branch.
    public static async Task ForcePushAsync(JourneyShell journey)
    {
        var shell = journey.Shell;
        var work = journey.Fixture.Root;
        var force = shell.FileStatusPage.ForcePush;
        Assert.True(force.Summary.Shows);
        Assert.Equal("feature no longer matches origin/feature", force.Summary.Title);
        force.RequestForcePushCommand.Execute(null);
        Assert.True(force.Replace.IsOpen);
        await force.Replace.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(force.Replace.IsOpen, force.Replace.Message);
        var head = await journey.Git(work, "rev-parse", "feature");
        Assert.Equal(head, await journey.Git(Path.Combine(journey.Fixture.Container, "hub.git"), "rev-parse", "refs/heads/feature"));
        Assert.Equal(head, await journey.Git(work, "rev-parse", "refs/remotes/origin/feature"));
        Assert.False(force.Summary.Shows);
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
    }
}
