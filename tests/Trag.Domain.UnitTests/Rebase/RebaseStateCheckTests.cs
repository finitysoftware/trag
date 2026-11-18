using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// How a rebase command is judged from the repository it left. A rewrite of the branch's own commits goes onto a commit the
// branch already has, so "onto is in the history" is true before Git does anything: a rewrite that changed nothing must
// still be told from one that finished.
public sealed class RebaseStateCheckTests
{
    private static readonly string Base = Snapshots.Id('b');
    private static readonly string Head = Snapshots.Id('d');
    private static readonly string Rewritten = Snapshots.Id('e');

    private static GitWriteResult Judge(RebaseStateCheck check, RepositorySnapshot after, bool onOnto, bool failed = false) =>
        check.Judge(after, onOnto, "details", failed, "onto", RebaseText.NotStarted);

    // A pre-rebase hook, a sequence editor that cannot run or a configuration Git rejects: Git exits before it moves the
    // branch, and the base is still an ancestor of the untouched HEAD.
    [Theory]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("")]
    public void WhenARewriteLeftTheBranchWhereItWas_ShouldRejectItInsteadOfCallingItFinished(string onto)
    {
        var check = new RebaseStateCheck("main", onto, Head, Head, null);

        var result = Judge(check, Snapshots.Create(head: Head), onOnto: true);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NotStarted), (result.Outcome, result.Message));
    }

    // Cancel pressed while Git was still starting: nothing changed, and that is what the outcome says.
    [Fact]
    public void WhenARewriteWasStoppedBeforeItChangedAnything_ShouldSayItStoppedBeforeAnyChange()
    {
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Create(head: Head), onOnto: true, failed: true);

        Assert.Equal((GitWriteOutcome.Cancelled, RebaseText.StoppedBeforeChange), (result.Outcome, result.Message));
    }

    // Skip puts the files of the stop back before it goes on, and a conflicted file Git rewrote there still reads as a
    // conflict: a skip Cancel stopped on a stop with conflicts may have changed what nothing in the repository shows.
    [Fact]
    public void WhenASkipOverConflictsWasStoppedWithTheStopAsItWas_ShouldAskForAReview()
    {
        var stop = Snapshots.Rebase(step: 2, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        var result = Judge(check, Snapshots.Rebasing(stop, [Snapshots.Conflict("f.txt")]) with { Head = Head }, onOnto: false, failed: true);

        Assert.Equal((GitWriteOutcome.Uncertain, WriteText.Interrupted), (result.Outcome, result.Message));
    }

    [Fact]
    public void WhenARewriteMovedTheBranchOntoItsBase_ShouldCallItFinished()
    {
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Create(head: Rewritten), onOnto: true);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
    }

    // An edit stop continued without amending finishes with HEAD where the stop left it: that is a finished rebase too.
    [Fact]
    public void WhenAContinuedRebaseFinishesWithHeadWhereItStopped_ShouldCallItFinished()
    {
        var stop = Snapshots.Rebase(step: 3, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        var result = Judge(check, Snapshots.Create(head: Head), onOnto: true);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
    }

    // A rebase onto another branch whose start Git refused: the onto commit is not in the untouched history.
    [Fact]
    public void WhenARebaseOntoAnotherBranchDidNotStart_ShouldRejectIt()
    {
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Create(head: Head), onOnto: false);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NotStarted), (result.Outcome, result.Message));
    }

    [Fact]
    public void WhenTheRebaseStoppedAtAConflict_ShouldAskForResolution()
    {
        var stop = Snapshots.Rebase(step: 1, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, null);

        var result = Judge(check, Snapshots.Rebasing(stop, [Snapshots.Conflict("f.txt")]), onOnto: false);

        Assert.Equal((GitWriteOutcome.NeedsResolution, "The rebase stopped at commit 1 of 3. Resolve its conflicts, then continue."), (result.Outcome, result.Message));
    }

    // Git also stops at a break line of a plan edited in a terminal, at a pick that became empty and before a pick that
    // would overwrite an untracked file; none of them leaves a conflict, and Git's reason is in Details.
    [Fact]
    public void WhenTheRebaseStoppedWithoutAConflict_ShouldNotAskToResolveConflicts()
    {
        var stop = Snapshots.Rebase(step: 4, total: 5, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, null);

        var result = Judge(check, Snapshots.Rebasing(stop), onOnto: false);

        Assert.Equal((GitWriteOutcome.NeedsResolution, "The rebase stopped at commit 4 of 5 without a conflict. Review Details for Git's reason, then continue."),
            (result.Outcome, result.Message));
    }

    // A Git stopped in the middle of a step leaves the rebase in progress without a stop of its own, so there is nothing to
    // resolve: the outcome asks for a review.
    [Fact]
    public void WhenTheRebaseWasStoppedWhileGitWasStillRunningIt_ShouldAskForAReviewInsteadOfAResolution()
    {
        var stop = Snapshots.Rebase(step: 1, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, null);

        var result = Judge(check, Snapshots.Rebasing(stop), onOnto: false, failed: true);

        Assert.Equal((GitWriteOutcome.Uncertain, WriteText.Interrupted), (result.Outcome, result.Message));
    }

    // Continue or Skip on a stop that Git left exactly as it was: refused, in the words the command gave.
    [Fact]
    public void WhenAStopIsSeenAgainUnchanged_ShouldRejectWithTheGivenReason()
    {
        var stop = Snapshots.Rebase(step: 2, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        var result = check.Judge(Snapshots.Rebasing(stop) with { Head = Head }, false, "details", false, "onto", RebaseText.NotContinued);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NotContinued), (result.Outcome, result.Message));
    }

    // Git could not sign a commit it replayed: before anything changed the rebase is refused, at a stop it waits there,
    // and either way the outcome says the commit was not signed.
    [Fact]
    public void WhenTheCommitCouldNotBeSigned_ShouldSaySo()
    {
        var stop = Snapshots.Rebase(step: 1, total: 3, branch: "main");
        const string details = "error: gpg failed to sign the data\nfatal: failed to write commit object";
        var unchanged = new RebaseStateCheck("main", Base, Head, Head, null)
            .Judge(Snapshots.Create(head: Head) with { Signing = CommitSigning.Ssh }, false, details, false, "onto", RebaseText.NotStarted);
        var stopped = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, null)
            .Judge(Snapshots.Rebasing(stop) with { Signing = CommitSigning.Ssh }, false, details, false, "onto", RebaseText.NotStarted);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NotSigned(CommitSigning.Ssh)), (unchanged.Outcome, unchanged.Message));
        Assert.Equal((GitWriteOutcome.NeedsResolution, RebaseText.NotSigned(CommitSigning.Ssh)), (stopped.Outcome, stopped.Message));
    }

    // A stop Git moved on from is not the stop the command was given, even with the same rebase in progress.
    [Fact]
    public void WhenTheStopMovedOnSinceTheCommand_ShouldNotCallItUnchanged()
    {
        var stop = Snapshots.Rebase(step: 2, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        var result = Judge(check, Snapshots.Rebasing(Snapshots.Rebase(step: 3, total: 3, branch: "main")) with { Head = Rewritten }, onOnto: false);

        Assert.Equal(GitWriteOutcome.NeedsResolution, result.Outcome);
    }

    // A plan edited in a terminal may stop at a break and then at an exec line that fails: Git ran a line, though the
    // commits, HEAD and the commit it stopped at are as they were.
    [Fact]
    public void WhenGitRanOnlyALineWithoutACommitAndStoppedAgain_ShouldNotCallTheStopUnchanged()
    {
        var stop = Snapshots.Rebase(step: 2, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        var result = Judge(check, Snapshots.Rebasing(stop with { LinesDone = stop.LinesDone + 1 }) with { Head = Head }, onOnto: false);

        Assert.Equal(GitWriteOutcome.NeedsResolution, result.Outcome);
    }

    // With no rebase left, only the branch on its onto commit is a finished rebase.
    [Fact]
    public void WhenNoRebaseIsLeftButTheBranchLacksItsOntoCommit_ShouldCallTheOutcomeUnexpected()
    {
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Create(head: Rewritten), onOnto: false);

        Assert.Equal((GitWriteOutcome.Uncertain, RebaseText.UnexpectedState), (result.Outcome, result.Message));
    }

    [Fact]
    public void WhenAnotherBranchIsCheckedOutOnTheOntoCommit_ShouldCallTheOutcomeUnexpected()
    {
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Create(branch: "other", head: Rewritten), onOnto: true);

        Assert.Equal((GitWriteOutcome.Uncertain, RebaseText.UnexpectedState), (result.Outcome, result.Message));
    }

    // Abort is done when no rebase is left and the branch is back on the commit it started from.
    [Fact]
    public void WhenTheBranchIsBackOnItsStartWithNoRebaseInProgress_ShouldCallItAborted()
    {
        var stop = Snapshots.Rebase(step: 2, total: 3, branch: "main");
        var check = new RebaseStateCheck("main", stop.Onto, stop.OrigHead, Head, stop);

        Assert.True(check.IsAborted(Snapshots.Create(head: stop.OrigHead)));
        Assert.False(check.IsAborted(Snapshots.Rebasing(stop) with { Branch = "main", Head = stop.OrigHead }));
        Assert.False(check.IsAborted(Snapshots.Create(branch: "other", head: stop.OrigHead)));
        Assert.False(check.IsAborted(Snapshots.Create(head: Rewritten)));
    }

    [Fact]
    public void WhenTheRepositoryIsInSomeoneElsesRebase_ShouldCallTheOutcomeUnexpected()
    {
        var other = Snapshots.Rebase(step: 1, total: 3, branch: "other");
        var check = new RebaseStateCheck("main", Base, Head, Head, null);

        var result = Judge(check, Snapshots.Rebasing(other), onOnto: false);

        Assert.Equal((GitWriteOutcome.Uncertain, RebaseText.UnexpectedState), (result.Outcome, result.Message));
    }
}
