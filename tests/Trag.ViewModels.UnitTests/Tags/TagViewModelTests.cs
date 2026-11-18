using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Tags;

public sealed class TagViewModelTests
{
    // The tag goes on the commit chosen in History, with the name trimmed and the message as it was typed.
    [Fact]
    public async Task WhenATagIsCreatedForTheChosenCommit_ShouldWriteItThereWithItsMessageAndClose()
    {
        var h = new ShellHarness();
        await using var model = await HistoryShownAsync(h);
        Assert.False(model.Tags.OpenCreateTagCommand.CanExecute(null));
        var commit = model.History.Page.Commits[1];
        model.History.SelectedCommit = commit; await model.History.Details.Completion;

        Assert.True(model.Tags.OpenCreateTagCommand.CanExecute(null));
        model.Tags.OpenCreateTagCommand.Execute(null);
        Assert.True(model.Tags.Draft.IsOpen);
        Assert.Equal("Create a tag at " + commit.ShortId + " · Initial.", model.Tags.Draft.Subtitle);
        Assert.Equal(("Message (optional)", "With a message, Git keeps an annotated tag with your name and the date; without one, a lightweight tag."),
            (model.Tags.Draft.MessageLabel, model.Tags.Draft.MessageHint));
        Assert.False(model.Tags.CreateTagCommand.CanExecute(null));
        model.Tags.Draft.Name = " v1.0 ";
        model.Tags.Draft.Message = "First release\n\nNotes.";
        Assert.True(model.Tags.CreateTagCommand.CanExecute(null));
        await model.Tags.CreateTagCommand.ExecuteAsync(null);

        var tag = Assert.IsType<TagRequest>(Assert.Single(h.Writer.Requests)).Tag;
        Assert.Equal(("v1.0", commit.Id, "First release\n\nNotes."), (tag.Name, tag.Commit, tag.Message));
        Assert.False(model.Tags.Draft.IsOpen);
    }

    // Git signs every tag in this repository, so the message is no longer optional and Create waits for one.
    [Fact]
    public async Task WhenTheRepositorySignsTags_ShouldAskForAMessageBeforeOfferingCreate()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Reader.Snapshot with { SignsTags = true };
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);
        model.Tags.Draft.Name = "v1.0";

        Assert.Equal(("Message", "Tags are signed in this repository, so a tag needs a message."), (model.Tags.Draft.MessageLabel, model.Tags.Draft.MessageHint));
        Assert.False(model.Tags.CreateTagCommand.CanExecute(null));
        model.Tags.Draft.Message = "Release";
        Assert.True(model.Tags.CreateTagCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenATagNameBreaksGitsRules_ShouldSayWhyAndOfferNoCreate()
    {
        var h = new ShellHarness();
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);

        model.Tags.Draft.Name = "v 1";

        Assert.Equal("Tag names cannot contain spaces.", model.Tags.Draft.NameError);
        Assert.True(model.Tags.Draft.HasNameError);
        Assert.False(model.Tags.CreateTagCommand.CanExecute(null));
    }

    // A tag Git refuses (here: the name is taken) keeps the dialog open with the reason; Cancel closes it and the next
    // one starts empty.
    [Fact]
    public async Task WhenGitRefusesTheTag_ShouldKeepTheDialogOpenWithTheReasonUntilCanceled()
    {
        var h = new ShellHarness();
        var refusal = "A tag named v1.0 already exists. Choose another name.";
        h.Writer.Result = new(GitWriteOutcome.Rejected, refusal);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);
        model.Tags.Draft.Name = "v1.0";

        await model.Tags.CreateTagCommand.ExecuteAsync(null);

        Assert.True(model.Tags.Draft.IsOpen);
        Assert.Equal(refusal, model.Tags.Draft.Notice);
        model.Tags.CancelCreateTagCommand.Execute(null);
        Assert.False(model.Tags.Draft.IsOpen);
        model.Tags.OpenCreateTagCommand.Execute(null);
        Assert.Equal(("", "", ""), (model.Tags.Draft.Name, model.Tags.Draft.Message, model.Tags.Draft.Notice));
    }

    // With a remote the dialog offers to push the tag there, checked; the push follows the tag it sends.
    [Fact]
    public async Task WhenTheRepositoryHasARemote_ShouldOfferToPushTheTagThereAndPushItRightAfterCreatingIt()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced();
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);

        Assert.True(model.Tags.Draft.CanPush); Assert.True(model.Tags.Draft.Push);
        Assert.Equal("Push the tag to origin", model.Tags.Draft.PushLabel);
        model.Tags.Draft.Name = "v1.0";
        await model.Tags.CreateTagCommand.ExecuteAsync(null);

        Assert.Equal(2, h.Writer.Requests.Count);
        Assert.IsType<TagRequest>(h.Writer.Requests[0]);
        var push = Assert.IsType<RemoteTagRequest>(h.Writer.Requests[1]);
        Assert.Equal(("origin", "v1.0", RemoteTagAction.Push), (push.Remote, push.Name, push.Action));
        Assert.False(model.Tags.Draft.IsOpen);
    }

    [Fact]
    public async Task WhenThePushIsUnchecked_ShouldCreateTheTagOnlyHere()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced();
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);
        model.Tags.Draft.Name = "v1.0";
        model.Tags.Draft.Push = false;

        await model.Tags.CreateTagCommand.ExecuteAsync(null);

        Assert.IsType<TagRequest>(Assert.Single(h.Writer.Requests));
    }

    // The tag exists once it is created, so a refused push closes the dialog all the same and says why in the banner.
    [Fact]
    public async Task WhenTheTagIsCreatedButThePushIsRefused_ShouldCloseTheDialogAndShowTheRefusal()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced();
        var refusal = "origin has a different tag named v1.0. Delete one of them or choose another name.";
        h.Writer.Handle = (request, _) => Task.FromResult(request is RemoteTagRequest
            ? new GitWriteResult(GitWriteOutcome.Rejected, refusal) : new GitWriteResult(GitWriteOutcome.Succeeded, "Tag v1.0 created."));
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Tags.OpenCreateTagCommand.Execute(null);
        model.Tags.Draft.Name = "v1.0";

        await model.Tags.CreateTagCommand.ExecuteAsync(null);

        Assert.False(model.Tags.Draft.IsOpen);
        Assert.Equal(refusal, model.StatusBar.ErrorMessage);
    }

    // Without a remote there is nowhere to push, so the dialog does not offer it.
    [Fact]
    public async Task WhenTheRepositoryHasNoRemote_ShouldNotOfferAPush()
    {
        var h = new ShellHarness();
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;

        model.Tags.OpenCreateTagCommand.Execute(null);

        Assert.False(model.Tags.Draft.CanPush);
    }

    private static async Task<ShellViewModel> HistoryShownAsync(ShellHarness h)
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        return model;
    }
}
