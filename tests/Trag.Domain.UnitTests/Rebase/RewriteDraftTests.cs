using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// The commits of a rewrite as they are edited: a reword's own message, a squash's joined message proposed and edited,
// a move or a change of what a commit does starting that message afresh, and the plan Git gets.
public sealed class RewriteDraftTests
{
    private static RewriteStep Step(char id) => new(Snapshots.Id(id), "Subject " + id, "Message " + id, RewriteAction.Pick, "Message " + id);

    private static RewriteDraft Draft() => RewriteDraft.Of([Step('b'), Step('c'), Step('d')]);

    private static string[] Messages(RewriteDraft draft) => [.. draft.Plan.Select(step => step.Message)];

    [Fact]
    public void WhenNothingIsEdited_ShouldPlanTheCommitsAsTheyAreAndSaySo()
    {
        var draft = Draft();

        Assert.Equal([Step('b'), Step('c'), Step('d')], draft.Plan);
        Assert.Equal(RewriteText.Unchanged, draft.Refusal);
        Assert.Equal((false, "Message b"), (draft.CanEditMessage(0), draft.MessageAt(0)));
    }

    // The new message stays with the commit while it is a reword, also after it was something else for a while.
    [Fact]
    public void WhenACommitIsReworded_ShouldGiveItTheNewMessageWhileItStaysAReword()
    {
        var reworded = Draft().With(1, RewriteAction.Reword).WithMessage(1, "New c");

        Assert.True(reworded.CanEditMessage(1));
        Assert.Equal(["Message b", "New c", "Message d"], Messages(reworded));
        Assert.Equal("", reworded.Refusal);
        var back = reworded.With(1, RewriteAction.Pick);
        Assert.Equal(["Message b", "Message c", "Message d"], Messages(back));
        Assert.Equal(RewriteText.Unchanged, back.Refusal);
        Assert.Equal("New c", back.With(1, RewriteAction.Reword).MessageAt(1));
    }

    // Every commit of the group shows the message the group gets, and an edit of it is what Git gets.
    [Fact]
    public void WhenACommitIsSquashed_ShouldProposeTheJoinedMessageAndSendTheEditedOne()
    {
        var squashed = Draft().With(1, RewriteAction.Squash);

        Assert.Equal((true, "Message b\n\nMessage c"), (squashed.CanEditMessage(1), squashed.MessageAt(1)));
        var edited = squashed.WithMessage(1, "Joined");
        Assert.Equal("Joined", edited.MessageAt(0));
        Assert.Equal(["Joined", "Message c", "Message d"], Messages(edited));
    }

    // A commit that leaves a squash takes its text out of the group it left: that group's edited message starts afresh
    // too, or the commit that left would be committed twice.
    [Fact]
    public void WhenASquashGoesBackToPick_ShouldProposeTheGroupItLeftAfresh()
    {
        var edited = Draft().With(1, RewriteAction.Squash).With(2, RewriteAction.Squash).WithMessage(0, "Joined b, c and d");

        var back = edited.With(2, RewriteAction.Pick);

        Assert.Equal(("Message b\n\nMessage c", "Message b\n\nMessage c"), (back.MessageAt(0), back.Plan[0].Message));
    }

    // A move can change which commits join which, and a change of what a commit does changes its group: both start the
    // joined message afresh.
    [Fact]
    public void WhenACommitIsMovedOrWhatOneDoesChanges_ShouldProposeTheJoinedMessageAfresh()
    {
        var edited = Draft().With(1, RewriteAction.Squash).WithMessage(0, "Joined");

        var moved = edited.Moved(2, 0);
        Assert.Equal([Snapshots.Id('d'), Snapshots.Id('b'), Snapshots.Id('c')], moved.Commits.Select(commit => commit.Id));
        Assert.Equal("Message b\n\nMessage c", moved.MessageAt(1));
        Assert.Equal("Message b\n\nMessage c\n\nMessage d", edited.With(2, RewriteAction.Squash).MessageAt(0));
        var group = Draft().With(1, RewriteAction.Squash).With(2, RewriteAction.Squash).WithMessage(0, "Joined");
        Assert.Equal("Message b\n\nMessage d\n\nMessage c", group.Moved(2, 1).MessageAt(0));
    }

    [Fact]
    public void WhenTheMessageIsNotOneTheRewriteSets_ShouldIgnoreAnEditOfIt()
    {
        var draft = Draft().WithMessage(0, "Ignored");

        Assert.Equal("Message b", draft.MessageAt(0));
        Assert.Equal(RewriteText.Unchanged, draft.Refusal);
        Assert.Equal("Message b", draft.With(0, RewriteAction.Reword).MessageAt(0));
    }

    // Every message Trag commits is committed verbatim, so the one a commit keeps or is given shows as it is written.
    [Fact]
    public void WhenACommitKeepsOrIsGivenItsOwnMessage_ShouldShowItAsWritten()
    {
        var draft = RewriteDraft.Of([new(Snapshots.Id('b'), "Subject", "Subject\n\nBody\n", RewriteAction.Pick, "Subject\n\nBody\n")]);

        Assert.Equal("Subject\n\nBody\n", draft.MessageAt(0));
        Assert.Equal("  New\n", draft.With(0, RewriteAction.Reword).WithMessage(0, "  New\n").MessageAt(0));
    }

    [Fact]
    public void WhenThePlanBreaksARule_ShouldSayWhy() =>
        Assert.Equal(RewriteText.AllDropped, Draft().With(0, RewriteAction.Drop).With(1, RewriteAction.Drop).With(2, RewriteAction.Drop).Refusal);
}
