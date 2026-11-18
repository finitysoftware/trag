using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Stash;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class BranchAndPublishTextTests
{
    [Fact]
    public void WhenTheBranchListIsCut_ShouldSayHowManyItShows() =>
        Assert.Equal("Showing the first 5,000 branches.", BranchText.ListCut);

    [Fact]
    public void WhenALocalBranchIsDeleted_ShouldAskToRemoveTheLocalBranchAndWarnItIsNotFullyMerged()
    {
        var local = LocalTopic();
        Assert.Equal("Delete branch", BranchText.DeleteTitle(local));
        Assert.Equal("Remove the local branch topic.", BranchText.DeleteSubtitle(local));
        Assert.StartsWith("topic is not fully merged.", BranchText.DeleteText(local));
    }

    [Fact]
    public void WhenARemoteBranchIsDeleted_ShouldAskToRemoveItFromItsRemote()
    {
        var remote = RemoteTopic();
        Assert.Equal("Delete remote branch", BranchText.DeleteTitle(remote));
        Assert.Equal("Remove origin/topic from origin.", BranchText.DeleteSubtitle(remote));
    }

    [Fact]
    public void WhenALocalBranchTracksAnUpstream_ShouldNameItInTheTooltip()
    {
        var local = LocalTopic();
        Assert.Equal("topic · tracks origin/topic", BranchText.Tooltip(local));
    }

    [Fact]
    public void WhenTheBranchIsRemote_ShouldShowItsWholeRefAsTheTooltip()
    {
        var remote = RemoteTopic();
        Assert.Equal("refs/remotes/origin/topic", BranchText.Tooltip(remote));
    }

    [Fact]
    public void WhenABranchIsCreatedWithoutAStartPoint_ShouldStartItFromTheCurrentBranch() =>
        Assert.Equal("Create a local branch from the current branch.", BranchText.CreateSubtitle(null));

    [Fact]
    public void WhenABranchIsCreatedFromAnotherBranch_ShouldNameThatBranch() =>
        Assert.Equal("Create a local branch from topic.", BranchText.CreateSubtitle(new("topic", "refs/heads/topic")));

    // A remote start point is named with its remote, so the subtitle says the new branch starts from what was fetched.
    [Fact]
    public void WhenABranchIsCreatedFromARemoteBranch_ShouldNameItWithItsRemote() =>
        Assert.Equal("Create a local branch from origin/release.", BranchText.CreateSubtitle(new("origin/release", "refs/remotes/origin/release")));

    [Fact]
    public void WhenPublishingToAnExistingRemote_ShouldBeTitledPublishBranch() =>
        Assert.Equal("Publish branch", PublishText.Title(PublishKind.ExistingRemote));

    [Fact]
    public void WhenPublishingCreatesARepository_ShouldAskWhereToPublishIt()
    {
        var context = Context();
        Assert.Equal("Choose where to publish trag.", PublishText.Subtitle(PublishKind.CreateRepository, context));
    }

    [Fact]
    public void WhenPublishingAddsARemote_ShouldSayItAddsTheRemoteAndSetsTheBranchToTrackIt()
    {
        var (context, input) = (Context(), AddRemote());
        Assert.Equal("Trag will add upstream with this address and set main to track it.", PublishText.Summary(input, context, "ignored"));
    }

    [Fact]
    public void WhenPublishingToAnExistingRemote_ShouldSummarizeWithTheTargetsSummary()
    {
        var (context, input) = (Context(), AddRemote());
        Assert.Equal("target", PublishText.Summary(input with { Kind = PublishKind.ExistingRemote }, context, "target"));
    }

    [Fact]
    public void WhenATargetBranchIsChosen_ShouldSayTheBranchIsPushedThereAndSetAsTheUpstream() =>
        Assert.Equal("Trag will push main to origin/shared and set it as the upstream.", PublishText.TargetSummary("main", "origin", "shared"));

    [Fact]
    public void WhenTheTargetIsAnotherBranchThatAlreadyExists_ShouldWarnThatTheCommitsArePushedIntoIt() =>
        Assert.Equal("origin/shared already exists. Commits of main will be pushed into it.", PublishText.TargetWarning(new("shared", "origin", false), "main"));

    [Fact]
    public void WhenNoTargetIsChosen_ShouldWarnOfNothing() => Assert.Equal("", PublishText.TargetWarning(null, "main"));

    [Fact]
    public void WhenTheTargetIsANewBranch_ShouldSayItIsNewOnTheRemote() =>
        Assert.Equal("main · new branch on origin", PublishText.TargetDisplay(new("main", "origin", true)));

    [Fact]
    public void WhenAStashEntryIsListed_ShouldSayItsBranchAndWhenItWasMade()
    {
        var entry = Entry();
        Assert.StartsWith("main · Today ", StashText.Detail(entry, entry.When));
    }

    [Fact]
    public void WhenAStashEntryShowsItsTooltip_ShouldGiveItsRefAndWholeMessage()
    {
        var entry = Entry();
        Assert.Equal("stash@{1} · On main: wip", StashText.Tooltip(entry));
    }

    [Fact]
    public void WhenAStashEntryIsDropped_ShouldAskAboutItsRefAndItsMessageWithoutTheBranch()
    {
        var entry = Entry();
        Assert.Equal("Remove stash@{1} · wip", StashText.DropQuestion(entry));
    }

    [Fact]
    public void WhenTheBranchIsUnknown_ShouldHintThatGitNamesAnUnnamedStashAfterTheCurrentBranch() =>
        Assert.Equal("Left empty, Git names it \"WIP on the current branch\".", StashText.DefaultHint(null));

    private static BranchInfo LocalTopic() => new("topic", "refs/heads/topic", "1", false, Upstream: "refs/remotes/origin/topic");

    private static BranchInfo RemoteTopic() => new("origin/topic", "refs/remotes/origin/topic", "1", true, "origin");

    private static PublishContext Context() => new(Snapshots.Create(), "trag");

    // The remote's name comes with the spaces the user typed around it.
    private static PublishInput AddRemote() => new(PublishKind.AddRemote, "trag", " upstream ", "", null, null, true);

    private static StashEntry Entry() => new(new string('s', 40), 1, "On main: wip", "main", DateTimeOffset.Now);
}
