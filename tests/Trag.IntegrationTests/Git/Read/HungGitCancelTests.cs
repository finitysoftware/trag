using System.Diagnostics;
using Trag.Domain.Presentation.Text;
using Trag.IntegrationTests.Journeys;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Git.Read;

// The reads the shell's Cancel reaches (opening and refreshing the repository, the history, a commit, its diff,
// the working diff and the blame) end when the user presses it while Git never answers, well before any limit of
// their own, and say so where the read shows. The shell runs over real Git until the read starts; Git then hangs.
public sealed class HungGitCancelTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WhenGitNeverAnswersWhileARepositoryOpens_ShouldEndTheOpenOnCancel()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        await CommitAsync(journey);
        journey.Host.Folder = journey.Fixture.Root;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        var elapsed = await CancelWhileGitHangsAsync(journey, () => shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null));

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(SessionText.Canceled(false), shell.StatusBar.Status);
        Assert.False(shell.Repository.HasRepository);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileTheRepositoryIsReadAgain_ShouldEndTheRefreshOnCancel()
    {
        await using var journey = await OpenAsync();
        var shell = journey.Shell;

        var elapsed = await CancelWhileGitHangsAsync(journey, () => shell.RefreshCommand.ExecuteAsync(null));

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(SessionText.Canceled(true), shell.StatusBar.Status);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileTheHistoryIsRead_ShouldEndTheReadOnCancel()
    {
        await using var journey = await OpenAsync();
        var shell = journey.Shell;

        var elapsed = await CancelWhileGitHangsAsync(journey, () =>
        {
            shell.Navigator.ShowHistoryCommand.Execute(null);
            return shell.History.HistoryCompletion;
        });

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(HistoryText.Canceled, shell.History.Page.Message);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileACommitIsRead_ShouldEndTheReadOnCancel()
    {
        await using var journey = await OpenAsync();
        var shell = journey.Shell;
        shell.Navigator.ShowHistoryCommand.Execute(null);
        await shell.History.HistoryCompletion;
        var details = shell.History.Details;

        var elapsed = await CancelWhileGitHangsAsync(journey, () =>
        {
            shell.History.SelectedCommit = shell.History.Page.Commits[0];
            return details.Completion;
        });

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(CommitText.LoadCanceled, details.Panel.Heading);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileACommitsDiffIsRead_ShouldEndTheReadOnCancel()
    {
        await using var journey = await OpenAsync();
        var shell = journey.Shell;
        shell.Navigator.ShowHistoryCommand.Execute(null);
        await shell.History.HistoryCompletion;
        var details = shell.History.Details;
        shell.History.SelectedCommit = shell.History.Page.Commits[0];
        await details.Completion;

        var elapsed = await CancelWhileGitHangsAsync(journey, () =>
        {
            details.Panel.SelectedFile = details.Panel.Files[0];
            return details.Diff.Completion;
        });

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(DiffMessages.Commit.Cancelled, details.Diff.Document.Message);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileAWorkingFilesDiffIsRead_ShouldEndTheReadOnCancel()
    {
        // The edit is there before the repository opens, so the watcher has no change pending when Git hangs.
        await using var journey = await OpenAsync(edit: "a\nb\n");
        var shell = journey.Shell;
        Assert.Single(shell.FileStatus.UnstagedFiles);

        var elapsed = await CancelWhileGitHangsAsync(journey, () =>
        {
            shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
            return shell.WorkingDiff.Diff.Completion;
        });

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.Equal(DiffMessages.Working.Cancelled, shell.WorkingDiff.Diff.Document.Message);
    }

    [Fact]
    public async Task WhenGitNeverAnswersWhileAFileIsBlamed_ShouldEndTheReadOnCancel()
    {
        await using var journey = await OpenAsync();
        var shell = journey.Shell;

        var elapsed = await CancelWhileGitHangsAsync(journey, () => shell.Blame.OpenAsync("f.txt", null));

        Assert.True(elapsed < Soon, elapsed.ToString());
        Assert.False(shell.Blame.IsBusy);
        Assert.Empty(shell.Blame.Rows);
    }

    private static async Task CommitAsync(JourneyShell journey)
    {
        await journey.Fixture.WriteAsync("f.txt", "a\n");
        await journey.Fixture.CommitAsync("Base", "f.txt");
    }

    private static async Task<JourneyShell> OpenAsync(string? edit = null)
    {
        var journey = await JourneyShell.StartAsync();
        await CommitAsync(journey);
        if (edit is not null) await journey.Fixture.WriteAsync("f.txt", edit);
        journey.Host.Folder = journey.Fixture.Root;
        await journey.Shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await journey.Shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await journey.Shell.Sync.SyncCompletion;
        return journey;
    }

    // Starts the read over a Git that never answers, presses Cancel once that Git runs, and gives how long the read went on
    // after it. Real Git comes back for what follows.
    private static async Task<TimeSpan> CancelWhileGitHangsAsync(JourneyShell journey, Func<Task> read)
    {
        var hung = await HungGit.CreateAsync(journey.Fixture.Container, journey.Fixture.Token);
        var real = journey.Executable.Path;
        journey.Executable.Path = hung.Program;
        try
        {
            var reading = read();
            await hung.StartedAsync(journey.Fixture.Token);
            Assert.True(journey.Shell.CancelCommand.CanExecute(null));
            var watch = Stopwatch.StartNew();
            journey.Shell.CancelCommand.Execute(null);
            await reading.WaitAsync(journey.Fixture.Token);
            return watch.Elapsed;
        }
        finally
        {
            journey.Executable.Path = real;
        }
    }
}
