using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// Rewriting the current branch's commits from one of them: what a plan may not ask, and the message a squash proposes.
public sealed class RewritePlanTests
{
    private static RewriteStep Step(char id, RewriteAction action = RewriteAction.Pick, string? message = null) =>
        new(Snapshots.Id(id), "Subject " + id, "Message " + id, action, message ?? "Message " + id);

    [Fact]
    public void WhenCommitsAreReorderedSquashedAndDropped_ShouldAllowThePlan() =>
        Assert.Null(RewritePlan.Refusal([Step('c'), Step('b', RewriteAction.Squash), Step('d', RewriteAction.Drop), Step('e', RewriteAction.Fixup)]));

    // A squash after a dropped commit joins the commit kept before the drop, as Git folds it into the last commit picked.
    [Fact]
    public void WhenASquashFollowsADroppedCommit_ShouldJoinTheCommitKeptBeforeTheDrop()
    {
        RewriteStep[] steps = [Step('b'), Step('c', RewriteAction.Drop), Step('d', RewriteAction.Squash)];
        Assert.Equal(0, RewritePlan.HeadOf(steps, 2));
        Assert.Null(RewritePlan.Refusal(steps));
    }

    [Fact]
    public void WhenEveryCommitIsDropped_ShouldRefuseAndPointToReset() =>
        Assert.Equal(RewriteText.AllDropped, RewritePlan.Refusal([Step('b', RewriteAction.Drop), Step('c', RewriteAction.Drop)]));

    // A dropped commit before it does not count: the first commit kept has nothing to join.
    [Theory]
    [InlineData(RewriteAction.Squash)]
    [InlineData(RewriteAction.Fixup)]
    public void WhenTheFirstCommitKeptIsFolded_ShouldRefuseThePlan(RewriteAction action) =>
        Assert.Equal(RewriteText.FirstFolded, RewritePlan.Refusal([Step('b', RewriteAction.Drop), Step('c', action), Step('d')]));

    [Theory]
    [InlineData(RewriteAction.Reword)]
    [InlineData(RewriteAction.Pick)]
    public void WhenAMessageItGivesIsBlank_ShouldRefuseThePlan(RewriteAction action) =>
        Assert.Equal(RewriteText.EmptyMessage, RewritePlan.Refusal([Step('b', action, " \n "), Step('c')]));

    // A commit Git made without a message may stay as it is; joined with another, the joined one needs a message.
    [Fact]
    public void WhenASquashedGroupIsLeftWithABlankMessage_ShouldRefuseThePlan()
    {
        var blank = new RewriteStep(Snapshots.Id('b'), "", "", RewriteAction.Pick, "");

        Assert.Null(RewritePlan.Refusal([blank, Step('c')]));
        Assert.Equal(RewriteText.EmptyMessage, RewritePlan.Refusal([blank, Step('c', RewriteAction.Squash)]));
    }

    // Git joins the messages of the squashed commits after the first one's, a blank line apart; a fixup's goes.
    [Fact]
    public void WhenCommitsAreSquashedIntoOne_ShouldProposeTheirMessagesJoined()
    {
        IReadOnlyList<RewriteStep> steps =
        [
            Step('b', RewriteAction.Reword, "New b\n"), Step('c', RewriteAction.Squash), Step('d', RewriteAction.Fixup),
            Step('e', RewriteAction.Drop), Step('f', RewriteAction.Squash, "Edited f"), Step('g'), Step('h', RewriteAction.Squash)
        ];

        Assert.Equal("New b\n\nMessage c\n\nEdited f", RewritePlan.DefaultMessage(steps, 0));
        Assert.Equal("Message g\n\nMessage h", RewritePlan.DefaultMessage(steps, 5));
    }

    // A squashed or fixed-up commit joins the commit kept before it, past dropped ones; the others stand for themselves.
    [Fact]
    public void WhenACommitIsAskedAbout_ShouldNameTheCommitWhoseMessageItsGroupGets()
    {
        IReadOnlyList<RewriteStep> steps = [Step('b'), Step('c', RewriteAction.Squash), Step('d', RewriteAction.Drop), Step('e', RewriteAction.Fixup), Step('f')];

        Assert.Equal([0, 0, 2, 0, 4], Enumerable.Range(0, steps.Count).Select(index => RewritePlan.HeadOf(steps, index)));
    }

    // A group with a squash gets a joined message; one with only fixups keeps its first commit's.
    [Fact]
    public void WhenAGroupIsAskedAbout_ShouldSayWhetherASquashJoinsIt()
    {
        IReadOnlyList<RewriteStep> steps = [Step('b'), Step('c', RewriteAction.Fixup), Step('d', RewriteAction.Drop), Step('e', RewriteAction.Squash), Step('f'), Step('g', RewriteAction.Fixup)];

        Assert.True(RewritePlan.HasSquash(steps, 0));
        Assert.False(RewritePlan.HasSquash(steps, 2));
        Assert.False(RewritePlan.HasSquash(steps, 4));
    }

    // The plan refuses a first commit folded into nothing; until it changes, that commit stands for itself.
    [Fact]
    public void WhenTheFirstCommitIsFolded_ShouldLetItStandForItself() =>
        Assert.Equal(1, RewritePlan.HeadOf([Step('b', RewriteAction.Drop), Step('c', RewriteAction.Fixup)], 1));

    // Nothing changes while every commit is picked in its own place with its own message.
    [Fact]
    public void WhenThePlanIsComparedWithTheCommitsAsTheyAre_ShouldSayWhetherAnythingChanges()
    {
        IReadOnlyList<RewriteStep> original = [Step('b'), Step('c')];

        Assert.True(RewritePlan.IsUnchanged(original, [Step('b'), Step('c')]));
        Assert.True(RewritePlan.IsUnchanged(original, [Step('b', RewriteAction.Reword), Step('c')]));
        Assert.False(RewritePlan.IsUnchanged(original, [Step('c'), Step('b')]));
        Assert.False(RewritePlan.IsUnchanged(original, [Step('b'), Step('c', RewriteAction.Drop)]));
        Assert.False(RewritePlan.IsUnchanged(original, [Step('b', RewriteAction.Reword, "New b"), Step('c')]));
    }
}
