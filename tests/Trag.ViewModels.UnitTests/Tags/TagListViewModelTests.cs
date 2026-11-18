using Trag.Domain.Presentation;
using Trag.Domain.Tags;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Tags;

// The sidebar's tag list: its rows and filter, finding a tag's commit in the history, pushing a tag and deleting one.
public sealed class TagListViewModelTests
{
    private static readonly TagInfo Ten = new("v1.10", Snapshots.HeadId, "Release ten");
    private static readonly TagInfo Nine = new("v1.9", Snapshots.Id('b'), "Initial");
    private static readonly TagInfo Light = new("light", Snapshots.Id('b'), "Initial");

    [Fact]
    public async Task WhenTheRepositoryHasTags_ShouldListThemAndFilterThemByName()
    {
        var h = Harness();
        await using var model = await OpenedAsync(h);

        Assert.True(model.Tags.HasTags);
        Assert.Equal(["v1.10", "v1.9", "light"], model.Tags.Visible.Select(row => row.Title));
        Assert.Equal(("v1.10", "3"), (model.Tags.TagButtonText, model.Tags.TagCountText));
        Assert.Equal(Snapshots.HeadId[..7] + " · Release ten", model.Tags.Visible[0].Detail);
        model.Tags.TagSearch = "v1.";
        Assert.Equal(["v1.10", "v1.9"], model.Tags.Visible.Select(row => row.Title));
        model.Tags.TagSearch = "LIGHT";
        Assert.Equal(["light"], model.Tags.Visible.Select(row => row.Title));
    }

    // The search finds a commit by its id, so a tag's commit is shown there, chosen, whatever page it is on.
    [Fact]
    public async Task WhenATagIsChosen_ShouldSearchTheHistoryForItsCommitAndChooseIt()
    {
        var h = Harness();
        await using var model = await OpenedAsync(h);
        model.Tags.IsPickerOpen = true;

        await model.Tags.ShowCommand.ExecuteAsync(model.Tags.Visible[1]);

        Assert.False(model.Tags.IsPickerOpen);
        Assert.Equal(WorkspaceSection.Search, model.Navigator.Section);
        Assert.Equal(Nine.Commit, model.History.SearchQuery);
        Assert.Equal(Nine.Commit, h.History.LastQuery);
        Assert.Equal(Nine.Commit, model.History.SelectedCommit?.Id);
    }

    // A search already shown searches again for the tag's commit at once.
    [Fact]
    public async Task WhenATagIsChosenWhileTheSearchIsShown_ShouldSearchForItsCommitThere()
    {
        var h = Harness();
        await using var model = await OpenedAsync(h);
        model.Navigator.ShowSearchCommand.Execute(null);
        model.History.SearchQuery = "something else"; await model.History.HistoryCompletion;

        await model.Tags.ShowCommand.ExecuteAsync(model.Tags.Visible[0]);

        Assert.Equal(WorkspaceSection.Search, model.Navigator.Section);
        Assert.Equal(Ten.Commit, h.History.LastQuery);
        Assert.Equal(Ten.Commit, model.History.SelectedCommit?.Id);
    }

    [Fact]
    public async Task WhenATagIsPushedFromItsRow_ShouldPushItToTheRemote()
    {
        var h = Harness(remote: true);
        await using var model = await OpenedAsync(h);

        Assert.True(model.Tags.CanPushTags);
        Assert.Equal("Push the tag to origin", model.Tags.PushTip);
        await model.Tags.PushCommand.ExecuteAsync(model.Tags.Visible[0]);

        var push = Assert.IsType<RemoteTagRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("origin", "v1.10", RemoteTagAction.Push), (push.Remote, push.Name, push.Action));
    }

    // Tags go to origin, or to the first remote when there is no origin, and the tip names the remote they go to.
    [Fact]
    public async Task WhenTheRepositoryHasNoOrigin_ShouldPushTheTagToItsFirstRemoteAndNameIt()
    {
        var h = Harness();
        h.Reader.Snapshot = h.Reader.Snapshot! with { Remotes = [Snapshots.Remote("backup"), Snapshots.Remote("mirror")] };
        await using var model = await OpenedAsync(h);

        Assert.Equal("Push the tag to backup", model.Tags.PushTip);
        await model.Tags.PushCommand.ExecuteAsync(model.Tags.Visible[0]);

        var push = Assert.IsType<RemoteTagRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("backup", "v1.10", RemoteTagAction.Push), (push.Remote, push.Name, push.Action));
    }

    [Fact]
    public async Task WhenTheRepositoryHasNoRemote_ShouldOfferNoPushAndNoRemoteDelete()
    {
        var h = Harness();
        await using var model = await OpenedAsync(h);

        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[0]);

        Assert.False(model.Tags.CanPushTags);
        Assert.False(model.Tags.CanPushTags);
    }

    // Deleting asks first; it takes the tag here only unless the user also asks for the remote.
    [Fact]
    public async Task WhenATagIsDeletedAfterTheQuestion_ShouldDeleteItHereOnly()
    {
        var h = Harness(remote: true);
        await using var model = await OpenedAsync(h);

        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[1]);
        Assert.True(model.Tags.Delete.IsOpen);
        Assert.Equal("Delete tag v1.9?", model.Tags.DeleteTitle);
        Assert.True(model.Tags.CanPushTags); Assert.False(model.Tags.DeleteOnRemote);
        Assert.Equal("Also delete it on origin", model.Tags.DeleteOnRemoteLabel);
        Assert.Equal("A tag left on origin comes back with the next fetch.", model.Tags.DeleteOnRemoteHint);
        await model.Tags.Delete.ConfirmCommand.ExecuteAsync(null);

        var delete = Assert.IsType<TagDeleteRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal("v1.9", delete.Name);
        Assert.False(model.Tags.Delete.IsOpen);
    }

    // The remote goes first, so a tag the remote kept is still here to try again.
    [Fact]
    public async Task WhenATagIsAlsoDeletedOnTheRemote_ShouldDeleteItThereFirstAndThenHere()
    {
        var h = Harness(remote: true);
        await using var model = await OpenedAsync(h);
        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[1]);
        model.Tags.DeleteOnRemote = true;

        await model.Tags.Delete.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(2, h.Writer.Requests.Count);
        var remote = Assert.IsType<RemoteTagRequest>(h.Writer.Requests[0]);
        Assert.Equal(("origin", "v1.9", RemoteTagAction.Delete), (remote.Remote, remote.Name, remote.Action));
        Assert.Equal("v1.9", Assert.IsType<TagDeleteRequest>(h.Writer.Requests[1]).Name);
    }

    [Fact]
    public async Task WhenTheRemoteRefusesTheDelete_ShouldKeepTheTagHereAndTheQuestionOpenWithTheReason()
    {
        var h = Harness(remote: true);
        var refusal = "origin refused tag v1.9: the tag is protected.";
        h.Writer.Result = new(GitWriteOutcome.Rejected, refusal);
        await using var model = await OpenedAsync(h);
        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[1]);
        model.Tags.DeleteOnRemote = true;

        await model.Tags.Delete.ConfirmCommand.ExecuteAsync(null);

        Assert.IsType<RemoteTagRequest>(Assert.Single(h.Writer.Requests));
        Assert.True(model.Tags.Delete.IsOpen);
        Assert.Equal(refusal, model.Tags.Delete.Message);
    }

    // A tag deleted behind Trag's back leaves nothing to ask about.
    [Fact]
    public async Task WhenTheTagInQuestionLeavesTheList_ShouldCloseTheQuestion()
    {
        var h = Harness();
        await using var model = await OpenedAsync(h);
        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[1]);

        h.Branches.Tags.Remove(Nine);
        h.Reader.Snapshot = h.Reader.Snapshot with { TagRefs = "two" };
        await model.RefreshCommand.ExecuteAsync(null); await model.Tags.List.Completion;

        Assert.False(model.Tags.Delete.IsOpen);
        Assert.Equal(["v1.10", "light"], model.Tags.Visible.Select(row => row.Title));
    }

    private static ShellHarness Harness(bool remote = false)
    {
        var h = new ShellHarness();
        h.Branches.Tags.AddRange([Ten, Nine, Light]);
        h.Reader.Snapshot = (remote ? h.Synced() : Snapshots.Create()) with { TagRefs = "three" };
        return h;
    }

    private static async Task<ShellViewModel> OpenedAsync(ShellHarness h)
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Tags.List.Completion;
        return model;
    }
}
