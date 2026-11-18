using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// A new project from an empty folder to its first publish, over real Git.
public sealed class FirstPublishJourneyTests
{
    [Fact]
    public async Task WhenAnEmptyFolderIsInitializedCommittedAndPublishedToAnExistingRemote_ShouldTrackItAndPushTheNextCommit()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var container = journey.Fixture.Container;
        var project = Path.Combine(container, "new project");
        var hub = Path.Combine(container, "hub.git");
        Directory.CreateDirectory(project);
        await journey.Git(container, "init", "--bare", "hub.git");

        // Repositories → New: the folder is not a repository yet, and initializing it lists and selects it.
        journey.Host.Folder = project;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(CatalogText.InitializeQuestion(project), shell.Catalog.Local.Initialization.Text);
        await shell.Catalog.Local.Initialization.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("new project", shell.Catalog.State.SelectedLocal!.Name);
        Assert.Equal("true", await journey.Git(project, "rev-parse", "--is-inside-work-tree"));
        Assert.NotEqual(0, (await journey.Fixture.RunAtAsync(project, "rev-parse", "--verify", "--quiet", "HEAD")).ExitCode);
        var branch = await journey.Git(project, "symbolic-ref", "--short", "HEAD");

        // The identity a user's machine has in its global configuration, which the suite's leaves out.
        await journey.Git(project, "config", "user.name", "Journey User");
        await journey.Git(project, "config", "user.email", "journey@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(project, "README.md"), "# New project\n", journey.Fixture.Token);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.True(shell.Repository.Snapshot!.IsUnborn);

        // The first commit, on the unborn branch.
        shell.FileStatus.SelectedUnstaged = (StatusRow)Assert.Single(shell.FileStatus.UnstagedFiles)!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        Assert.Equal("README.md", await journey.Git(project, "diff", "--cached", "--name-only"));
        shell.Commit.CommitMessage = "Start the project";
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var first = await journey.Git(project, "rev-parse", "HEAD");
        Assert.Equal("Start the project", await journey.Git(project, "log", "--format=%s"));
        Assert.Equal("", await journey.Git(project, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal(first, shell.Repository.Snapshot!.Head);

        // Publish → Add an existing remote URL: the bare remote on disk, pushed to with its upstream set.
        Assert.True(shell.Publish.Context.ShowPublish);
        await shell.Publish.OpenPublishCommand.ExecuteAsync(null);
        shell.Publish.UsePublishUrlCommand.Execute(null);
        shell.Publish.Draft.RemoteUrl = hub;
        Assert.True(shell.Publish.Draft.PushAfter);
        await shell.Publish.PublishCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Publish.Draft.IsOpen, shell.Publish.Draft.Notice + shell.Publish.PublishDetails);
        Assert.Equal(hub, await journey.Git(project, "remote", "get-url", "origin"));
        Assert.Equal("origin/" + branch, await journey.Git(project, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"));
        Assert.Equal(first, await journey.Git(hub, "rev-parse", "refs/heads/" + branch));
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.Equal("↑ 0 ahead  ·  ↓ 0 behind", shell.Sync.Panel.Counts.Text);
        Assert.False(shell.Publish.Context.ShowPublish);

        // A second commit, which the watcher shows: one ahead, then pushed.
        await File.AppendAllTextAsync(Path.Combine(project, "README.md"), "What it is for.\n", journey.Fixture.Token);
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the edited README.md");
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await shell.Commit.StageSelectedCommand.ExecuteAsync(null);
        shell.Commit.CommitMessage = "Describe the project";
        await shell.Commit.CommitCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        var second = await journey.Git(project, "rev-parse", "HEAD");
        Assert.Equal(first, await journey.Git(project, "rev-parse", "HEAD^"));
        Assert.Equal("1", await journey.Git(project, "rev-list", "--count", "@{upstream}..HEAD"));
        Assert.Equal((1, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.True(shell.Sync.PushCommand.CanExecute(null));
        Assert.Equal("Push · 1", shell.Sync.Panel.Counts.PushLabel);
        await shell.Sync.PushCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(second, await journey.Git(hub, "rev-parse", "refs/heads/" + branch));
        Assert.Equal(second, await journey.Git(project, "rev-parse", "refs/remotes/origin/" + branch));
        Assert.Equal((0, 0), (shell.Sync.Panel.Counts.Ahead, shell.Sync.Panel.Counts.Behind));
        Assert.Equal("↑ 0 ahead  ·  ↓ 0 behind", shell.Sync.Panel.Counts.Text);
        Assert.Equal("", await journey.Git(project, "--no-optional-locks", "status", "--porcelain"));
        await journey.Git(project, "fsck", "--no-progress");
        await journey.Git(hub, "fsck", "--no-progress");
    }
}
