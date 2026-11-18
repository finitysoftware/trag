using Trag.Application.Sync;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Sync;

public sealed class SyncPanelTests
{
    [Fact]
    public void WhenNoRemoteIsChosen_ShouldShowNoCountsAccountsOrPushDestination()
    {
        var empty = SyncOverview.Empty();
        Assert.Null(empty.State);
        Assert.False(empty.CanSynchronize);
        Assert.Equal("Choose a remote", empty.Target.FetchAddress);
        Assert.Equal("Choose a remote and upstream branch", empty.Target.PushDestination);
        Assert.False(empty.Target.HasWarning);
        Assert.Contains("unknown", empty.Counts.Text);
        Assert.Equal("Push", empty.Counts.PushLabel);
        Assert.Equal("Pull", empty.Counts.PullLabel);
        Assert.False(empty.Counts.HasFetchedText);
        Assert.Empty(empty.Accounts.Items);
        Assert.Null(empty.Accounts.Selected);
    }

    [Fact]
    public void WhenARemoteIsChosenButNotRead_ShouldShowItsFetchAddressAndStillNoPushDestination()
    {
        var empty = SyncOverview.Empty();
        var selected = SyncOverview.Empty(new("origin", "fetch", "push"));
        Assert.Equal("fetch", selected.Target.FetchAddress);
        Assert.Equal(empty.Target.PushDestination, selected.Target.PushDestination);
    }

    [Theory]
    [InlineData(null, null, "Ahead / behind unknown", "Push", "Pull")]
    [InlineData(2, null, "Ahead / behind unknown", "Push · 2", "Pull")]
    [InlineData(0, 0, "↑ 0 ahead  ·  ↓ 0 behind", "Push", "Pull")]
    [InlineData(2, 3, "↑ 2 ahead  ·  ↓ 3 behind", "Push · 2", "Pull · 3")]
    public void WhenTheCountsAreShown_ShouldTellUnknownZeroAndPositiveApart(int? ahead, int? behind, string text, string push, string pull)
    {
        var state = State() with { Ahead = ahead, Behind = behind };
        var counts = SyncCounts.From(state, null, DateTimeOffset.UnixEpoch);
        Assert.Equal(text, counts.Text);
        Assert.Equal(push, counts.PushLabel);
        Assert.Equal(pull, counts.PullLabel);
        Assert.Equal(ahead is > 0, counts.HasPushCount);
        Assert.Equal(behind is > 0, counts.HasPullCount);
    }

    [Theory]
    [InlineData("trunk", false, false)]
    [InlineData("shared", false, true)]
    [InlineData("shared", true, false)]
    public void WhenTheTargetIsShown_ShouldWarnOnlyWhenPushingIntoAnotherExistingBranch(string branch, bool isNew, bool warning)
    {
        var selected = new SyncBranch("refs/heads/" + branch, "refs/remotes/origin/" + branch) { IsNew = isNew };
        var state = State() with { Branch = selected, Branches = [selected] };
        var panel = SyncOverview.From(Snapshots.Create(branch: "trunk"), state, null, DateTimeOffset.UnixEpoch);
        Assert.Equal("fetch", panel.Target.FetchAddress);
        Assert.Equal("trunk → refs/heads/" + branch + "\npush", panel.Target.PushDestination);
        Assert.Equal(warning, panel.Target.HasWarning);
        if (warning) Assert.Equal("Push sends trunk into origin/shared.", panel.Target.Warning);
        if (isNew) Assert.Equal("shared is not on origin yet", panel.Counts.Text);
    }

    [Fact]
    public void WhenALimitedRemoteHasNoBranchChosen_ShouldNotInventAUsablePushTarget()
    {
        var state = State() with { Branch = null, Remote = new("origin", "fetch", "push", "config", "Unsupported remote") };
        var panel = SyncOverview.From(Snapshots.Create(), state, null, DateTimeOffset.UnixEpoch);
        Assert.Equal("Choose an upstream branch", panel.Target.PushDestination);
        Assert.False(panel.Target.HasWarning);
        Assert.False(panel.CanSynchronize);
    }

    [Fact]
    public void WhenTheTargetIsReadAgain_ShouldKeepShowingTheLastResult()
    {
        var panel = SyncOverview.From(Snapshots.Create(), State(), null, DateTimeOffset.UnixEpoch);
        var reading = panel.Reading();
        Assert.Same(panel.State, reading.State);
        Assert.Same(panel.Counts, reading.Counts);
        Assert.Same(panel.Accounts, reading.Accounts);
        Assert.Contains("Reading", reading.Message);
    }

    // The remotes say when the target was fetched; an overview without a target has nothing to say it about.
    [Fact]
    public void WhenTheTargetWasFetched_ShouldSayWhenAndKeepTheCounts()
    {
        var panel = SyncOverview.From(Snapshots.Create(), State(), null, DateTimeOffset.UnixEpoch);
        Assert.False(panel.Counts.HasFetchedText);
        var fetched = panel.Fetched(new DateTimeOffset(2026, 9, 14, 16, 43, 12, TimeSpan.Zero));
        Assert.StartsWith("Fetched ", fetched.Counts.FetchedText);
        Assert.Equal((panel.Counts.Text, panel.State, panel.Accounts), (fetched.Counts.Text, fetched.State, fetched.Accounts));
        Assert.False(SyncOverview.Empty().Fetched(DateTimeOffset.UnixEpoch).Counts.HasFetchedText);
    }

    [Fact]
    public void WhenTheSavedAccountIsSelected_ShouldBeConfirmedWithNothingToSave()
    {
        var choices = Choices();
        var model = new SyncAccountSelection(new(choices, "alice", "Saved"));
        Assert.True(model.IsConfirmed);
        Assert.False(model.IsUnsaved);
    }

    // Only the saved identity is confirmed.
    [Fact]
    public void WhenTheUserSelectsAnotherAccount_ShouldOfferToSaveItAndNotConfirmIt()
    {
        var choices = Choices();
        var model = new SyncAccountSelection(new(choices, "alice", "Saved"));
        var changed = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SyncAccountSelection.Selected)) changed++; };

        model.Selected = choices[1];

        Assert.True(model.IsUnsaved);
        Assert.True(model.CanSave);
        Assert.False(model.IsConfirmed);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void WhenTheSavedAccountWasRemoved_ShouldNotBeConfirmed()
    {
        var choices = Choices();
        Assert.False(new SyncAccountSelection(new(choices, "removed", "Removed")).IsConfirmed);
    }

    [Fact]
    public void WhenNoAccountIsSaved_ShouldNotBeConfirmed()
    {
        var choices = Choices();
        Assert.False(new SyncAccountSelection(new(choices, null, "Missing")).IsConfirmed);
    }

    [Fact]
    public void WhenTheRemoteHasNoAccountState_ShouldBeConfirmed()
    {
        Assert.True(new SyncAccountSelection(null).IsConfirmed);
    }

    private static RepositorySyncState State()
    {
        var branch = new SyncBranch("refs/heads/trunk", "refs/remotes/origin/trunk");
        return new(new("origin", "fetch", "push", "config"), [branch], branch, 2, 1, "Local comparison");
    }

    private static AccountChoice[] Choices() => [new("alice", "Alice"), new("bob", "Bob")];
}
