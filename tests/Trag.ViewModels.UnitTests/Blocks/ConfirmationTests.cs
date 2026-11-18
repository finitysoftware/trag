using Trag.Application.Live;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.UnitTests.Blocks;

public sealed class ConfirmationTests
{
    [Fact]
    public async Task WhenTheUserAnswersAQuestion_ShouldCloseItOnSuccessOrCancelAndKeepItOpenWithTheReasonOtherwise()
    {
        var repository = new Repository(hasWriter: true); repository.Apply(Snapshots.Create());
        var performed = new List<string>();
        var result = new GitWriteResult(GitWriteOutcome.Succeeded, "Dropped.");
        var confirmation = new Confirmation<string>(repository, subject => { performed.Add(subject); return Task.FromResult<GitWriteResult?>(result); },
            subject => "Remove " + subject, "Could not drop.");
        Assert.False(confirmation.IsOpen); Assert.Equal("", confirmation.Text); Assert.False(confirmation.ConfirmCommand.CanExecute(null));

        confirmation.Request("spike");
        Assert.True(confirmation.IsOpen); Assert.Equal("Remove spike", confirmation.Text); Assert.True(confirmation.ConfirmCommand.CanExecute(null));
        confirmation.CancelCommand.Execute(null);
        Assert.False(confirmation.IsOpen); Assert.Empty(performed);

        confirmation.Request("spike");
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(["spike"], performed); Assert.False(confirmation.IsOpen); Assert.Equal("", confirmation.Message);

        // Git refused: the question stays with the reason; no result at all: with the given one.
        result = new(GitWriteOutcome.Rejected, "Git refused.");
        confirmation.Request("spike");
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.True(confirmation.IsOpen); Assert.Equal("Git refused.", confirmation.Message);
        result = null!;
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.True(confirmation.IsOpen); Assert.Equal("Could not drop.", confirmation.Message);
        confirmation.Request("other"); // asking again starts clean
        Assert.Equal("", confirmation.Message); Assert.Equal("Remove other", confirmation.Text);
    }

    // A write that stops at conflicts is done with the question: the operation's own view takes over from there.
    [Fact]
    public async Task WhenTheWriteStopsAtConflicts_ShouldCloseTheQuestion()
    {
        var repository = new Repository(hasWriter: true); repository.Apply(Snapshots.Create());
        var confirmation = new Confirmation<string>(repository, _ => Task.FromResult<GitWriteResult?>(new(GitWriteOutcome.NeedsResolution, "Stopped at conflicts.")),
            subject => "Revert " + subject, "Could not revert.");

        confirmation.Request("abc");
        await confirmation.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal((false, ""), (confirmation.IsOpen, confirmation.Message));
    }

    [Fact]
    public async Task WhenAWriteRunsOrTheQuestionIsClosed_ShouldHoldConfirmAndLetCancelWaitForTheRunningWrite()
    {
        var repository = new Repository(hasWriter: true); repository.Apply(Snapshots.Create());
        var pending = new TaskCompletionSource<GitWriteResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new Confirmation<string>(repository, _ => pending.Task, subject => subject, "failed");
        var announced = 0;
        confirmation.ConfirmCommand.CanExecuteChanged += (_, _) => announced++;
        confirmation.Request("spike");

        repository.IsWriting = true;
        Assert.False(confirmation.ConfirmCommand.CanExecute(null)); Assert.True(announced > 0); // the requery reached it
        repository.IsWriting = false;
        Assert.True(confirmation.ConfirmCommand.CanExecute(null));

        var confirming = confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.True(confirmation.IsRunning); Assert.False(confirmation.ConfirmCommand.CanExecute(null));
        confirmation.CancelCommand.Execute(null);
        Assert.True(confirmation.IsOpen); // too late to cancel, the write is running
        pending.SetResult(new(GitWriteOutcome.Succeeded, "Done."));
        await confirming;
        Assert.False(confirmation.IsRunning); Assert.False(confirmation.IsOpen);

        confirmation.Request("spike");
        confirmation.Close();
        Assert.False(confirmation.IsOpen);
        await confirmation.ConfirmCommand.ExecuteAsync(null); // nothing to confirm
        Assert.False(confirmation.IsRunning);
    }

    // An action answers with the reason to stay open, or null when it is done.
    [Fact]
    public async Task WhenTheActionIsNotAGitWrite_ShouldFollowItsOwnPreconditionAndStayOpenOnlyWithAReason()
    {
        var repository = new Repository(); // no writer, no snapshot: a write-based question could never run here
        var busy = false;
        var reason = "Still in use.";
        var confirmation = new Confirmation<string>(repository, _ => Task.FromResult<string?>(reason), subject => "Forget " + subject, allowed: () => !busy);
        confirmation.Request("entry");
        Assert.True(confirmation.ConfirmCommand.CanExecute(null));
        busy = true;
        Assert.False(confirmation.ConfirmCommand.CanExecute(null));
        busy = false;
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.True(confirmation.IsOpen); Assert.Equal("Still in use.", confirmation.Message);
        reason = null!;
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.False(confirmation.IsOpen); Assert.Equal("", confirmation.Message);
    }

    // A question over a repository says what is about to happen there, so a terminal that switches the branch while it is
    // open has it say so again for the branch it would now act on.
    [Fact]
    public void WhenTheRepositoryChangesUnderAnOpenQuestion_ShouldSayItsTextAgain()
    {
        var repository = new Repository(hasWriter: true); repository.Apply(Snapshots.Create());
        var confirmation = new Confirmation<string>(repository, _ => Task.FromResult<GitWriteResult?>(null),
            subject => "Revert " + subject + " on " + repository.Snapshot!.Branch, "Could not revert.");
        confirmation.Request("spike");
        var said = new List<string>();
        confirmation.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(confirmation.Text)) said.Add(confirmation.Text); };

        repository.Apply(Snapshots.Create(branch: "topic", head: Snapshots.Id('e')));

        Assert.Equal(["Revert spike on topic"], said);
    }

    // The picker's questions have no repository: the owner supplies the precondition and re-asks the command itself.
    [Fact]
    public async Task WhenAQuestionHasNoRepository_ShouldStillAskAndConfirm()
    {
        var removed = new List<string>();
        var confirmation = new Confirmation<string>(subject => { removed.Add(subject); return Task.FromResult<string?>(null); }, subject => "Remove " + subject + "?", () => true);
        confirmation.Request("entry");
        Assert.Equal("Remove entry?", confirmation.Text);
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(["entry"], removed); Assert.False(confirmation.IsOpen);
    }
}
