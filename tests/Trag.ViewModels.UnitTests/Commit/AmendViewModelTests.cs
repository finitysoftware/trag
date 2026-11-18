using Trag.Domain.History;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Commit;

// Amending the last commit from the commit pane: the box starts from that commit's message and keeps the draft aside,
// the commit command replaces the commit, and the draft comes back when the amend is done or turned off.
public sealed class AmendViewModelTests
{
    private const string LastMessage = "Last subject\n\nIts body";

    // Nothing is staged: an amend may only reword the commit.
    [Fact]
    public async Task WhenAmendIsTurnedOn_ShouldStartFromTheLastCommitsMessageAndReplaceItWithNothingStaged()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Draft of the next commit";
        Assert.True(model.Commit.CanAmend); Assert.False(model.Commit.CommitCommand.CanExecute(null));

        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;

        Assert.Equal(LastMessage, model.Commit.CommitMessage); Assert.Equal("", model.Commit.AmendWarning);
        Assert.True(model.Commit.CommitCommand.CanExecute(null));
        model.Commit.CommitMessage = "Better subject";
        await model.Commit.CommitCommand.ExecuteAsync(null);
        var request = Assert.IsType<CommitRequest>(Assert.Single(h.Writer.Requests));
        Assert.True(request.Amend); Assert.Equal("Better subject", request.Message);
        Assert.False(model.Commit.IsAmending); Assert.Equal("Draft of the next commit", model.Commit.CommitMessage);
    }

    [Fact]
    public async Task WhenAmendIsTurnedOff_ShouldBringTheDraftBackAndCommitAsBefore()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Draft";
        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;

        model.Commit.IsAmending = false;

        Assert.Equal("Draft", model.Commit.CommitMessage);
        Assert.False(model.Commit.CommitCommand.CanExecute(null));
    }

    // Turned off before the last commit's message arrives: the message that arrives late leaves the draft as it is.
    [Fact]
    public async Task WhenAmendIsTurnedOffBeforeTheLastMessageArrives_ShouldKeepTheDraft()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Draft";
        h.History.Gate.IsClosed = true;

        model.Commit.IsAmending = true;
        model.Commit.IsAmending = false;
        h.History.Gate.Open(); await model.Commit.AmendCompletion;

        Assert.Equal((false, "Draft"), (model.Commit.IsAmending, model.Commit.CommitMessage));
    }

    // HEAD moved under the pane, by a commit made elsewhere: the message is no longer the last commit's.
    [Fact]
    public async Task WhenHeadMovesWhileAmending_ShouldTurnAmendOffAndBringTheDraftBack()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Draft";
        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;

        h.Reader.Snapshot = Snapshots.Create(head: Snapshots.Id('c'));
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.False(model.Commit.IsAmending); Assert.Equal("Draft", model.Commit.CommitMessage);
    }

    [Fact]
    public async Task WhenTheLastCommitIsAlreadyOnTheUpstream_ShouldWarnThatPushWillHaveToReplaceIt()
    {
        var h = Harness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Equal("", model.Commit.AmendWarning);

        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;

        Assert.Equal("This commit is already on origin/main. After amending, Push will have to replace it there.", model.Commit.AmendWarning);
    }

    // Git keeps the author of the commit an amend replaces and stamps the user only as its committer; the pane says so
    // while amending a commit someone else wrote, and names the user again once the amend is off.
    [Fact]
    public async Task WhenAmendingSomeoneElsesCommit_ShouldShowTheAuthorGitKeeps()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(("Author: Fixture <fixture@example.invalid>", ""), (model.Commit.AuthorText, model.Commit.CommitterText));
        var shown = new List<string>();
        model.Commit.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Commit.AuthorText)) shown.Add(model.Commit.AuthorText); };

        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;
        var amending = (model.Commit.AuthorText, model.Commit.CommitterText);
        model.Commit.IsAmending = false;

        Assert.Equal(("Author: Author <author@example.invalid>", "Committer: Fixture <fixture@example.invalid>"), amending);
        Assert.Equal(("Author: Fixture <fixture@example.invalid>", ""), (model.Commit.AuthorText, model.Commit.CommitterText));
        Assert.Equal(["Author: Author <author@example.invalid>", "Author: Fixture <fixture@example.invalid>"], shown);
    }

    [Fact]
    public async Task WhenTheRepositoryHasNoCommitYet_ShouldOfferNoAmend()
    {
        var h = Harness(); h.Reader.Snapshot = Snapshots.Create(unborn: true);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.False(model.Commit.CanAmend);
    }

    // A clean repository whose last commit has a subject and a body.
    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create();
        h.History.Details = id => new CommitDetails(Snapshots.Commit(id, "Last subject"), LastMessage, []);
        return h;
    }
}
