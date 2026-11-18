using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;
using Trag.Domain.Status;

namespace Trag.ViewModels.UnitTests.Commit;

public sealed class WriteViewModelTests
{
    [Theory]
    [InlineData(GitWriteOutcome.Rejected)]
    [InlineData(GitWriteOutcome.Cancelled)]
    [InlineData(GitWriteOutcome.Uncertain)]
    public async Task WhenACommitFailsOrEndsUncertain_ShouldKeepTheMessageAndAskForReviewWhenUncertain(GitWriteOutcome outcome)
    {
        var backend = Backend(); backend.Result = new(outcome, "Operation result");
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Keep this message\n\nBody";
        Assert.True(model.Commit.CommitCommand.CanExecute(null));
        await model.Commit.CommitCommand.ExecuteAsync(null);
        Assert.Equal("Keep this message\n\nBody", model.Commit.CommitMessage);
        Assert.Equal(outcome == GitWriteOutcome.Uncertain, model.Repository.NeedsReview);
        Assert.Equal(outcome != GitWriteOutcome.Uncertain, model.Commit.CommitCommand.CanExecute(null));
        if (outcome == GitWriteOutcome.Uncertain)
        {
            await model.RefreshCommand.ExecuteAsync(null);
            Assert.True(model.Commit.CommitCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task WhenACommitIsConfirmed_ShouldClearTheSubmittedMessage()
    {
        const string id = "0123456789012345678901234567890123456789";
        var backend = Backend(); backend.Result = new(GitWriteOutcome.Succeeded, "Committed", CommitId: id);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Commit only the index";
        await model.Commit.CommitCommand.ExecuteAsync(null);
        Assert.Empty(model.Commit.CommitMessage);
        Assert.IsType<CommitRequest>(backend.LastRequest);
        Assert.Equal("Commit only the index", ((CommitRequest)backend.LastRequest!).Message);
    }

    // The renamed file changed after its rename was staged: the change is staged under the new name alone.
    [Fact]
    public async Task WhenAGroupIsStaged_ShouldNameAStagedRenameByItsNewPathOnlyAndBlockOtherWritesUntilVerified()
    {
        var backend = Backend(); backend.Gate.IsClosed = true;
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[0]);
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[1]);
        Assert.Equal("Stage 2 files", model.Commit.StageActionText);
        var pending = model.Commit.StageSelectedCommand.ExecuteAsync(null);
        await backend.Gate.Started;
        Assert.False(model.Commit.UnstageSelectedCommand.CanExecute(null));
        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.False(model.CloseRepositoryCommand.CanExecute(null));
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.True(model.Repository.HasRepository);
        Assert.Equal(["new.txt", "other.txt"], Assert.IsType<StageRequest>(backend.LastRequest).Paths.Select(path => path.Display));
        model.CancelCommand.Execute(null);
        Assert.True(backend.Gate.Token.IsCancellationRequested);
        Assert.True(model.Repository.IsWriting); // Cancel waits for the verified outcome.
        backend.Result = new(GitWriteOutcome.Cancelled, "Verified cancellation"); backend.Gate.Open();
        await pending;
        Assert.False(model.Repository.IsWriting);
        Assert.True(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheMessageIsInvalidOrTheAuthorIsMissing_ShouldRefuseTheCommitButKeepStagingAvailable()
    {
        var backend = Backend();
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        foreach (var message in new[] { "", " \n\t", "a\0b", new string('č', 32769) })
        {
            model.Commit.CommitMessage = message;
            Assert.False(model.Commit.CommitCommand.CanExecute(null));
        }
        // Git's own angle-bracket form, which used to be a property on the Domain identity record.
        Assert.Equal("Author: Fixture <fixture@example.invalid>", model.Commit.AuthorText);

        backend.Snapshot = backend.Snapshot with { Author = null };
        await model.RefreshCommand.ExecuteAsync(null);
        model.Commit.CommitMessage = "Valid message";
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        Assert.False(model.Commit.CommitCommand.CanExecute(null));
        Assert.True(model.Commit.StageSelectedCommand.CanExecute(null));
        Assert.Contains("user.name", model.Commit.AuthorText);
    }

    [Fact]
    public async Task WhenTheShellClosesDuringAWrite_ShouldCancelTheWrite()
    {
        var backend = Backend(); backend.Gate.IsClosed = true;
        var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[0]);
        var pending = model.Commit.StageSelectedCommand.ExecuteAsync(null);
        await backend.Gate.Started;
        await model.DisposeAsync();
        Assert.True(backend.Gate.Token.IsCancellationRequested);
        backend.Gate.Open();
        await pending;
    }

    [Fact]
    public async Task WhenTheRepositorySignsCommits_ShouldSaySoInTheCommitPane()
    {
        var backend = Backend(); backend.Snapshot = backend.Snapshot with { Signing = CommitSigning.Ssh };
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("Commits are signed with your SSH key.", model.Commit.SigningText);
    }

    [Fact]
    public async Task WhenTheSigningConfigurationChanges_ShouldAnnounceTheNewLine()
    {
        var backend = Backend();
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        var changed = new List<string?>();
        model.Commit.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        backend.Snapshot = backend.Snapshot with { Signing = CommitSigning.OpenPgp };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Contains(nameof(model.Commit.SigningText), changed);
        Assert.Equal("Commits are signed with your GPG key.", model.Commit.SigningText);
    }

    private static ShellViewModel Create(FakeRepositoryWorkspace backend) => ShellHarness.Compose(backend);
    [Fact]
    public async Task WhenChangesAreDiscarded_ShouldAskFirstNameTheFilesAndSendOnlyTheirPaths()
    {
        var backend = Backend();
        backend.Snapshot = backend.Snapshot with { Files = [.. backend.Snapshot.Files, new(new("scratch.txt"u8.ToArray()), '?', '?')] };
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.False(model.Discard.DiscardSelectedCommand.CanExecute(null)); Assert.False(model.Discard.Confirmation.IsOpen);
        model.FileStatus.SelectedUnstaged = model.FileStatus.UnstagedFiles.Cast<StatusRow>().Single(row => row.PathText == "other.txt");
        Assert.True(model.Discard.DiscardSelectedCommand.CanExecute(null));
        model.Discard.DiscardSelectedCommand.Execute(null);
        Assert.True(model.Discard.Confirmation.IsOpen); Assert.Equal("Discard changes?", model.Discard.Confirmation.Subject!.Title); Assert.Equal("other.txt", model.Discard.Confirmation.Subject!.Subtitle);
        Assert.Contains("what is staged stays staged", model.Discard.Confirmation.Subject!.Description); Assert.DoesNotContain("untracked", model.Discard.Confirmation.Subject!.Description);
        model.Discard.Confirmation.CancelCommand.Execute(null);
        Assert.False(model.Discard.Confirmation.IsOpen); Assert.Null(backend.LastRequest);

        // Two files including an untracked one: the text says it will be deleted, a rejection keeps the dialog open, success closes it.
        model.FileStatus.UnstagedSelection.Add(model.FileStatus.UnstagedFiles.Cast<StatusRow>().Single(row => row.PathText == "other.txt"));
        model.FileStatus.UnstagedSelection.Add(model.FileStatus.UnstagedFiles.Cast<StatusRow>().Single(row => row.PathText == "scratch.txt"));
        model.Discard.DiscardSelectedCommand.Execute(null);
        Assert.Equal("Discard changes in 2 files?", model.Discard.Confirmation.Subject!.Title); Assert.Contains("1 untracked file is deleted", model.Discard.Confirmation.Subject!.Description);
        backend.Result = new(GitWriteOutcome.Rejected, "Git refused.");
        await model.Discard.Confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.Discard.Confirmation.IsOpen); Assert.Equal("Git refused.", model.Discard.Confirmation.Message);
        Assert.Equal(["other.txt", "scratch.txt"], Assert.IsType<DiscardRequest>(backend.LastRequest).Paths.Select(path => path.Display));
        backend.Result = new(GitWriteOutcome.Succeeded, "Discarded changes in 2 files.");
        await model.Discard.Confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.False(model.Discard.Confirmation.IsOpen); Assert.Equal("", model.Discard.Confirmation.Message); Assert.Contains("Discarded", model.StatusBar.Status); Assert.Empty(model.FileStatus.UnstagedSelection);
    }
    // A rename staged in the index and a plain modification beside it: enough for every selection the pane offers.
    private static FakeRepositoryWorkspace Backend() =>
        new() { Snapshot = Snapshots.Create(head: "head", files: [Snapshots.Renamed("old.txt", "new.txt", 'M'), Snapshots.Modified("other.txt")]) };
}
