using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// A rebase stop as the rebase view, the file status and History describe it: the sides named yours and upstream (Git's
// theirs and ours), what is left and what keeps Continue back, the plan, and what the rest of the workspace says.
public sealed class RebaseOverviewTests
{
    private static RepositorySnapshot Stopped(int step, MergeResolutions resolutions, params FileStatus[] files) =>
        Snapshots.Rebasing(Snapshots.Rebase(step: step, total: 7), files) with { Resolutions = resolutions };

    // Editing commits from the first one has no commit to go onto: the rebase starts the history anew.
    [Fact]
    public void WhenARebaseHasNoBase_ShouldSayItGoesOntoTheStartOfTheHistory()
    {
        var rebase = Snapshots.Rebase(ontoName: null) with { Onto = "" };

        Assert.Equal("Rebasing feature onto the start of its history", RebaseText.Rebasing(rebase));
    }

    [Fact]
    public void WhenARebaseStops_ShouldNameYoursTheReplayedCommitAndUpstreamWhatItLandsOn()
    {
        var sides = ConflictSides.For(Stopped(3, MergeResolutions.None));
        Assert.Equal(("YOURS · commit 3 of 7 · 3333333 Commit 3", ConflictChoice.Theirs), (sides.Yours.Title, sides.Yours.Git));
        Assert.Equal(("UPSTREAM · main + commits 1–2", ConflictChoice.Mine), (sides.Other.Title, sides.Other.Git));
    }

    [Fact]
    public void WhenBlocksAreLeftInARebaseConflict_ShouldCountThemAndNameTheSides()
    {
        var sides = ConflictSides.For(Stopped(3, MergeResolutions.None));
        Assert.Equal("Conflict · 2 blocks left · yours = commit 3 of 7 · upstream = main",
            MergeText.ConflictLine(new(Snapshots.Conflict("f.cs"), 2), sides));
    }

    [Fact]
    public void WhenUpstreamDeletedAFileYoursChanged_ShouldOfferToKeepYoursOrDeleteItWithUpstream()
    {
        var sides = ConflictSides.For(Stopped(3, MergeResolutions.None));
        var byUpstream = new MergeConflict(new(Snapshots.Path("Legacy.cs"), 'D', 'U', IsConflict: true), null);
        Assert.Equal("Use yours to keep the version from commit 3 of 7, or Use upstream to delete the file, as main did.", MergeText.DeletionChoice(byUpstream, sides));
    }

    [Fact]
    public void WhenAFileIsResolvedWithYours_ShouldTagItYoursAndSayWhatTheCommitChanges()
    {
        var sides = ConflictSides.For(Stopped(3, MergeResolutions.None));
        var took = new MergeResolved(Snapshots.Path("f.cs"), Resolution.Theirs, false, Snapshots.Staged("f.cs"));
        Assert.Equal(("yours", "Resolved · took yours"), (MergeText.Tag(took, sides), MergeText.ResolvedTitle(took, sides)));
        Assert.Equal("Resolved with your side. The diff shows what this commit changes on main.", MergeText.ResolvedLine(took, sides));
    }

    [Fact]
    public void WhenAFileKeepsUpstreamWithNothingStaged_ShouldTagItUpstreamAndSayTheCommitLeavesItAsOnMain()
    {
        var sides = ConflictSides.For(Stopped(3, MergeResolutions.None));
        var took = new MergeResolved(Snapshots.Path("f.cs"), Resolution.Theirs, false, Snapshots.Staged("f.cs"));
        var kept = took with { How = Resolution.Mine, Staged = null };
        Assert.Equal(("upstream", "This commit leaves this file as it is on main."), (MergeText.Tag(kept, sides), MergeText.ResolvedLine(kept, sides)));
    }

    [Fact]
    public void WhenOneCommitWasAppliedBeforeTheStop_ShouldNameUpstreamWithThatOneCommit()
    {
        Assert.Equal("main + commit 1", ConflictSides.For(Stopped(2, MergeResolutions.None)).Other.Name);
    }

    [Fact]
    public void WhenAStopHasAConflict_ShouldSayWhatIsLeftAndKeepContinueBack()
    {
        var open = ConflictedStop();
        Assert.Equal(("feature onto main", "commit 3 of 7 stopped: 1 file is in conflict."), (open.Direction, open.Remaining));
        Assert.Equal(("2 of 7 commits applied", "3/7"), (open.ProgressText, open.SidebarCount));
        Assert.Equal("Continue is available once every conflict in this commit is resolved · 1 left", open.ContinueHint);
        Assert.True(open.IsBlocked); Assert.True(open.ShowsFileStatusBanner);
        Assert.Equal("commit 3 of 7 has 1 file in conflict. Conflicts are resolved in the rebase view, not here.", open.FileStatusBanner);
    }

    [Fact]
    public void WhenAStopHasAConflict_ShouldShowThePlanWithTheAppliedCommitsTheStopAndTheWaitingOnes()
    {
        var open = ConflictedStop();
        Assert.Equal([(RebaseStepState.Applied, "applied"), (RebaseStepState.Applied, "applied"), (RebaseStepState.Current, "stopped · conflict"),
            (RebaseStepState.Waiting, "waiting")], open.Steps.Take(4).Select(step => (step.State, step.Note)));
    }

    [Fact]
    public void WhenAStopHasAConflict_ShouldOfferTheCommitsMessageToEditBeforeContinuing()
    {
        var open = ConflictedStop();
        Assert.Equal("Commit 3 of 7 · its message; edit it here before continuing", open.MessageCaption);
    }

    [Fact]
    public void WhenEveryConflictOfTheStopIsResolved_ShouldSayContinueCommitsItAndReplaysTheRest()
    {
        var resolved = Stopped(3, new([new(Snapshots.Path("f.cs"), Resolution.Theirs)], []), Snapshots.Staged("f.cs"));
        var ready = new RebaseOverview(resolved, MergeFiles.From(resolved));
        Assert.False(ready.IsBlocked);
        Assert.Equal("commit 3 of 7: every conflict is resolved. Continue commits it and replays the 4 commits left.", ready.Remaining);
        Assert.Equal("ready to continue", ready.Steps[2].Note);
    }

    // Git put the fold it could not commit back in the plan: it is the stop and the first waiting line at once, and Continue
    // folds it with the message the plan gives it, not the one shown.
    [Fact]
    public void WhenGitCouldNotCommitAFold_ShouldSayContinueFoldsItWithThePlansMessageAndShowItOnce()
    {
        var rebase = Snapshots.Rebase(step: 3, total: 7);
        var putBack = rebase with { Todo = [rebase.Done[^1], .. rebase.Todo], FoldNotCommitted = true, FoldPutBack = true };
        var stop = Snapshots.Rebasing(putBack, [Snapshots.Staged("f.cs")]);

        var fold = new RebaseOverview(stop, MergeFiles.From(stop));

        Assert.Equal("commit 3 of 7: Git could not fold it into the commit before it. Continue folds it again and replays the 4 commits left.", fold.Remaining);
        Assert.Equal("Continue folds commit 3 of 7 into the commit before it with the message the plan gives, not this one.", fold.ContinueHint);
        Assert.Equal("Commit 3 of 7 · the fold keeps the plan's message", fold.MessageCaption);
        Assert.Equal(7, fold.Steps.Count);
    }

    // From Git 2.56 a fold whose commit failed stays done and Git stops to amend it: the plan holds no line of it, so every
    // waiting commit is shown, and Continue folds it as for one put back.
    [Fact]
    public void WhenGitStoppedToAmendAFoldItCouldNotCommit_ShouldShowEveryWaitingCommitAndSayContinueFoldsIt()
    {
        var stopped = Snapshots.Rebase(step: 3, total: 7) with { FoldNotCommitted = true };
        var stop = Snapshots.Rebasing(stopped, [Snapshots.Staged("f.cs")]);

        var fold = new RebaseOverview(stop, MergeFiles.From(stop));

        Assert.Equal("commit 3 of 7: Git could not fold it into the commit before it. Continue folds it again and replays the 4 commits left.", fold.Remaining);
        Assert.Equal(7, fold.Steps.Count);
    }

    // A fold Git put back that gets its message a second time when Git folds it again is finished in a terminal, so the
    // view promises nothing of Continue there.
    [Fact]
    public void WhenGitCouldNotCommitAFoldThatWouldRepeatItsMessage_ShouldSayItIsFinishedInATerminal()
    {
        var rebase = Snapshots.Rebase(step: 3, total: 7);
        var putBack = rebase with { Todo = [rebase.Done[^1], .. rebase.Todo], FoldNotCommitted = true, FoldPutBack = true, FoldRepeatsMessage = true };
        var stop = Snapshots.Rebasing(putBack, [Snapshots.Staged("f.cs")]);

        var fold = new RebaseOverview(stop, MergeFiles.From(stop));

        Assert.Equal("commit 3 of 7: Git could not fold it into the commit before it. Finish it in a terminal or abort.", fold.Remaining);
        Assert.Equal(RebaseText.FoldRepeatsMessage, fold.ContinueHint);
        Assert.Equal("Commit 3 of 7 · the message is set in Git's editor in a terminal", fold.MessageCaption);
        Assert.Equal("finish in a terminal", fold.Steps[2].Note);
    }

    // A plan changed in a terminal so that it no longer folds the commit Git put back first is finished there: the view
    // promises nothing of Continue and shows the plan as it now is. The commit's line may say drop, or be removed, which
    // leaves the next squash first, one that would repeat a message too.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhenThePlanOfAFoldGitCouldNotCommitWasChangedInATerminal_ShouldSayItIsFinishedThereAndShowThePlanAsItIs(bool removed)
    {
        var rebase = Snapshots.Rebase(step: 3, total: 7);
        var changed = rebase with
        {
            Todo = removed ? rebase.Todo : [rebase.Done[^1] with { Action = "drop" }, .. rebase.Todo],
            FoldNotCommitted = true, FoldPutBack = true, FoldRepeatsMessage = removed, FoldPlanChanged = true,
        };
        var stop = Snapshots.Rebasing(changed, [Snapshots.Staged("f.cs")]);

        var fold = new RebaseOverview(stop, MergeFiles.From(stop));

        var total = 3 + changed.Todo.Count;
        Assert.Equal("commit 3 of " + total + ": Git could not fold it into the commit before it, and its plan was changed in a terminal. Finish the rebase there or abort.", fold.Remaining);
        Assert.Equal(RebaseText.FoldPlanChanged, fold.ContinueHint);
        Assert.Equal("Commit 3 of " + total + " · the rebase is finished in a terminal", fold.MessageCaption);
        Assert.Equal(("finish in a terminal", 3 + changed.Todo.Count, changed.Todo[0]), (fold.Steps[2].Note, fold.Steps.Count, fold.Steps[3].Commit));
    }

    [Fact]
    public void WhenTheLastCommitStops_ShouldSayContinueFinishesTheRebase()
    {
        var last = Stopped(7, MergeResolutions.None);
        Assert.Equal("Continue commits the last commit with this message and finishes the rebase.", new RebaseOverview(last, MergeFiles.From(last)).ContinueHint);
    }

    [Fact]
    public void WhenARebaseStopsWithAConflict_ShouldSendTheFileStatusToTheRebaseView()
    {
        var conflicted = Stopped(3, MergeResolutions.None, Snapshots.Conflict("f.cs"));
        var scope = ConflictScope.For(conflicted, MergeFiles.From(conflicted));
        Assert.Equal((WorkspaceSection.Rebase, "Rebase in progress:", "Open rebase view", "Resolve in rebase view →"), (scope.Section, scope.Lead, scope.OpenText, scope.ResolveText));
        Assert.Equal("A rebase is in progress. To change a file's resolution, open the rebase view and choose Mark as unresolved.", scope.UnstageTip);
        Assert.Equal(scope.UnstageTip, ConflictScope.UnstageTipFor(conflicted));
        Assert.Equal("Remove selected whole files from the index; keep working files", ConflictScope.UnstageTipFor(Snapshots.Create()));
        Assert.Equal("Continue would commit conflict markers.", scope.MarkersLead);
        Assert.Equal(IconKinds.Rebase, scope.Icon);
    }

    [Fact]
    public void WhenNoMergeOrRebaseLeftTheConflicts_ShouldOfferNoViewForThem()
    {
        Assert.False(ConflictScope.For(Snapshots.Create(files: [Snapshots.Conflict("f.cs")]), MergeFiles.Empty).HasView);
    }

    [Fact]
    public void WhenARebaseIsInProgress_ShouldKeepHistoryFromGoingToAnotherCommitAndSayWhy()
    {
        var conflicted = Stopped(3, MergeResolutions.None, Snapshots.Conflict("f.cs"));
        var summary = new GoToSummary(null, null, conflicted);
        Assert.Equal(GoToBlock.Rebase, GoToCommit.Blocker(conflicted));
        Assert.Equal(("Finish or abort the rebase first", "Open rebase view"), (summary.BlockedTitle, summary.OperationViewText));
        Assert.Equal("A rebase of feature onto main is still in progress at commit 3 of 7. Moving to another commit waits until the rebase is finished or aborted.",
            summary.BlockedText);
    }

    // Commit 3 of 7 stopped with f.cs in conflict, one block left, and g.cs staged.
    private static RebaseOverview ConflictedStop()
    {
        var conflicted = Stopped(3, new([], [new(Snapshots.Path("f.cs"), 1)]), Snapshots.Conflict("f.cs"), Snapshots.Staged("g.cs"));
        return new RebaseOverview(conflicted, MergeFiles.From(conflicted));
    }
}
