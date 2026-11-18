using System.Windows.Input;
using Trag.Desktop.Composition;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Commit;

// What the commit pane makes of a write's outcome, from the write starting to its verified or undisplayable end.
public sealed class WriteOutcomeTests
{
    [Fact]
    public async Task WhenTheMessageIsEditedWhileTheCommitRuns_ShouldKeepTheNewWording()
    {
        var backend = Backend(); backend.Gate.IsClosed = true;
        backend.Result = new(GitWriteOutcome.Succeeded, "Committed", CommitId: Snapshots.Id('c'));
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "First wording";
        var pending = model.Commit.CommitCommand.ExecuteAsync(null); await backend.Gate.Started;
        model.Commit.CommitMessage = "Second wording";
        backend.Gate.Open(); await pending;
        Assert.Equal("Second wording", model.Commit.CommitMessage);
    }

    [Fact]
    public async Task WhenAWriteEndsUnverified_ShouldBlockEveryWriteUntilRefresh()
    {
        var backend = Backend(); backend.Result = new(GitWriteOutcome.Succeeded, "Committed", CommitId: Snapshots.Id('c'));
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Done";
        await model.Commit.CommitCommand.ExecuteAsync(null);

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        backend.Failure = new IOException("index.lock");
        await model.Commit.StageSelectedCommand.ExecuteAsync(null);
        foreach (var command in new ICommand[] { model.Commit.CommitCommand, model.Commit.StageSelectedCommand, model.Commit.UnstageSelectedCommand,
            model.Discard.DiscardSelectedCommand, model.Stash.OpenStashCommand, model.Merge.RequestAbortMergeCommand })
            Assert.False(command.CanExecute(null));

        backend.Failure = null;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("", model.StatusBar.ErrorMessage);
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        Assert.True(model.Commit.StageSelectedCommand.CanExecute(null)); Assert.True(model.Stash.OpenStashCommand.CanExecute(null)); Assert.True(model.Discard.DiscardSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheCommitterDiffersFromTheAuthorOrIsMissing_ShouldShowItAndRefuseTheCommitWhileItIsMissing()
    {
        var backend = Backend(); backend.Result = new(GitWriteOutcome.Succeeded, "Committed", CommitId: Snapshots.Id('c'));
        await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("", model.Commit.CommitterText);
        backend.Snapshot = backend.Snapshot with { Committer = new GitIdentity("Other", "other@example.invalid") };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Committer: Other <other@example.invalid>", model.Commit.CommitterText);
        backend.Snapshot = backend.Snapshot with { Committer = null };
        await model.RefreshCommand.ExecuteAsync(null);
        model.Commit.CommitMessage = "Valid";
        Assert.Equal("Committer identity is missing.", model.Commit.CommitterText); Assert.False(model.Commit.CommitCommand.CanExecute(null));
    }

    // Before the close the pane went through another committer, a missing one and a commit.
    [Fact]
    public async Task WhenTheRepositoryClosesAfterACommit_ShouldResetTheCommitPane()
    {
        var backend = Backend(); backend.Result = new(GitWriteOutcome.Succeeded, "Committed", CommitId: Snapshots.Id('c'));
        await using var model = ShellHarness.Compose(backend);
        await OpenWithAnOtherThenAMissingCommitterAsync(backend, model);

        backend.Snapshot = backend.Snapshot with { Committer = Snapshots.Identity };
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Commit.CommitCommand.ExecuteAsync(null);
        Assert.Equal("", model.Commit.CommitMessage);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Equal("", model.Commit.CommitterText); Assert.Equal("Git identity is not available.", model.Commit.AuthorText);
    }

    // Opens, reads a committer other than the author, then none, and words a message that would be valid.
    private static async Task OpenWithAnOtherThenAMissingCommitterAsync(FakeRepositoryWorkspace backend, ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        backend.Snapshot = backend.Snapshot with { Committer = new GitIdentity("Other", "other@example.invalid") };
        await model.RefreshCommand.ExecuteAsync(null);
        backend.Snapshot = backend.Snapshot with { Committer = null };
        await model.RefreshCommand.ExecuteAsync(null);
        model.Commit.CommitMessage = "Valid";
    }

    // The shell hands every repository it shows the record a write's defect goes to.
    [Fact]
    public async Task WhenAWriteThrows_ShouldRecordWhatItThrewWhereTheShellWasToldTo()
    {
        var backend = Backend();
        List<Exception> recorded = [];
        await using var model = ShellFactory.Compose(new FakeRepositoryCatalog(), new FakeLastRepositoryStore(), new FakeGitInstallations(), backend,
            defects: recorded.Add);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        backend.Failure = new InvalidOperationException("defect");

        await model.Commit.StageSelectedCommand.ExecuteAsync(null);

        Assert.Same(backend.Failure, Assert.Single(recorded));
    }

    // One staged and one unstaged file: enough for a commit, a stage and a discard.
    private static FakeRepositoryWorkspace Backend() =>
        new() { Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt"), Snapshots.Modified("b.txt")]) };
}
